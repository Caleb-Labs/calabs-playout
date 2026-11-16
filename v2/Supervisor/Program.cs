// CaLabs Playout 2.0 - Supervisor: mantiene vivos a CasparCG y al Programador, vigila que el video
// avance y, si no, pone la emergencia al aire. Si el Supervisor se cierra, lo demas sigue funcionando.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using CaLabs;

var raiz = Comun.Raiz();
var cfg = Comun.LeerConfig(raiz);
void Log(string m) => Comun.Log(raiz, "Supervisor", m);
const string NombreProg = "CaLabs.Programador";
var exeProg = Path.Combine(AppContext.BaseDirectory, NombreProg + ".exe");
string Datos(string f) => Path.Combine(raiz, "datos", f);
var rutaEmerg = Datos("emergencia-estado.json");
string CapaP() => $"{cfg.Canal}-{cfg.Capa}";
string CapaE() => $"{cfg.Canal}-{cfg.CapaEmergencia}";

// Un Programador de una sesion anterior chocaria con el nuevo: se cierra. A CasparCG no se le toca: puede estar al aire.
foreach (var p in Process.GetProcessesByName(NombreProg)) { try { p.Kill(); p.WaitForExit(3000); } catch { } }

var parrilla = new Hijo("Programador", "", Datos("estado.json"));
var emergProg = new Hijo("Emergencia", $"--nombre Emergencia --capa {cfg.CapaEmergencia} --lista \"{cfg.ListaEmergencia}\" --estado estado-emergencia.json --mando mando-emergencia.json --siempre", Datos("estado-emergencia.json"));
var emerg = Comun.Leer<Emergencia>(rutaEmerg) ?? new Emergencia();
Amcp? amcp = null;
var ultPos = -1.0;
var ultAvance = DateTime.Now;
var ordenes = new ConcurrentQueue<(string cmd, TaskCompletionSource<string> resp)>();
var fallos = 0;
var gracia = DateTime.MinValue;
var primera = true;
var caido = true;
var vivo = false;
var saliendo = false;
var hVentana = IntPtr.Zero;       // acceso al proceso de la ventana para leer su codigo de salida (.NET no lo da si no la abrio el supervisor)
Process? ventana = null;          // la ventana de la Parrilla, si esta abierta
var colgada = DateTime.MinValue;
var ultRespuesta = DateTime.Now;  // ultima vez que el motor contesto a una orden (el puerto abierto no basta: un motor congelado tambien lo acepta)
var ultPosE = -1.0;
var ultAvanceE = DateTime.Now;
NotifyIcon? bandeja = null;
var audioFalta = false;           // el dispositivo de audio del aire no esta conectado: el motor se queda sin sonido hasta reabrirlo
var audioVisto = DateTime.MinValue;
// Primera vez en un PC (recien instalado): se crean las carpetas de datos y la configuracion del motor, con rutas de esta instalacion.
try
{
    Directory.CreateDirectory(Datos("")); Directory.CreateDirectory(Datos("media"));
    var rc0 = Path.Combine(cfg.CasparDir, "casparcg.config");
    if (!File.Exists(rc0) && Directory.Exists(cfg.CasparDir))
    {
        var d = Datos("motor").Replace('\\', '/');
        var x0 = new XDocument(new XElement("configuration",
            new XElement("paths", new XElement("media-path", Datos("media").Replace('\\', '/') + "/"), new XElement("log-path", new XAttribute("disable", "false"), d + "/log/"), new XElement("data-path", d + "/data/"), new XElement("template-path", d + "/template/")),
            new XElement("lock-clear-phrase", "secret"), new XElement("log-level", "warning"),
            new XElement("ndi", new XElement("auto-load", "true")),
            new XElement("channels", new XElement("channel", new XElement("video-mode", "1080p2997"), new XElement("consumers",
                new XElement("screen", new XElement("stretch", "uniform"), new XElement("windowed", "true"), new XElement("width", 960), new XElement("height", 540)),
                new XElement("system-audio"), new XElement("ndi", new XElement("name", "CaLabs Playout"))))),
            new XElement("controllers", new XElement("tcp", new XElement("port", cfg.AmcpPort), new XElement("protocol", "AMCP")))));
        x0.Save(rc0);
    }
}
catch (Exception ex) { Log("No se pudo preparar la configuracion del motor: " + ex.Message); }
// La imagen se muestra siempre con su proporcion (con franjas si la ventana o el monitor son de otra forma), nunca estirada.
try
{
    var rc = Path.Combine(cfg.CasparDir, "casparcg.config"); var xc = XDocument.Load(rc); var sc = xc.Descendants("screen").FirstOrDefault();
    var cambio = false;
    if (sc != null && sc.Element("stretch") == null) { sc.Add(new XElement("stretch", "uniform")); cambio = true; }
    if (xc.Root!.Element("osc") == null) // el motor avisa del nivel de audio a la pantalla (vumetro)
    {
        xc.Root.Add(new XElement("osc", new XElement("default-port", 6250), new XElement("predefined-clients", new XElement("predefined-client", new XElement("address", "127.0.0.1"), new XElement("port", cfg.PuertoOsc)))));
        cambio = true;
    }
    if (xc.Root.Element("log-level") == null) { xc.Root.Add(new XElement("log-level", "warning")); cambio = true; } // sin el trafico de cada orden: 24 h al aire llenarian el disco
    if (cambio) xc.Save(rc);
}
catch (Exception ex) { Log("No se pudo revisar la configuracion de CasparCG: " + ex.Message); }
Log("Inicio." + (emerg.Activa ? " Sigue activa la emergencia: " + emerg.Motivo : ""));
if ((Comun.Leer<Lista>(Path.Combine(raiz, cfg.ListaEmergencia))?.Entradas.Length ?? 0) == 0)
    Log("AVISO: la lista de emergencia esta vacia. Si algo falla no habra nada que poner al aire.");
new Thread(Control) { IsBackground = true }.Start();
// Icono junto al reloj de Windows: se ve que el playout esta activo y se puede abrir la ventana de control o apagar todo.
new Thread(() =>
{
    try
    {
        Application.EnableVisualStyles();
        var ico = SystemIcons.Application;
        try { using var bmp = new Bitmap(new Bitmap(Path.Combine(raiz, "ui", "playout", "Logos", "CaLabs_icono_perfil_circular_1024.png")), 32, 32); ico = Icon.FromHandle(bmp.GetHicon()); } catch { }
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir ventana de control", null, (_, _) => AbrirVentana());
        menu.Items.Add("Abrir salida (reinicia el motor)", null, (_, _) => ordenes.Enqueue(("ABRIR", new TaskCompletionSource<string>())));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir de todo el playout…", null, (_, _) =>
        {
            if (MessageBox.Show("Se apaga todo el playout y el canal se queda sin señal.\n\n¿Salir?", "CaLabs Playout", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                ordenes.Enqueue(("CERRAR", new TaskCompletionSource<string>()));
        });
        bandeja = new NotifyIcon { Icon = ico, Text = "CaLabs Playout - al aire", Visible = true, ContextMenuStrip = menu };
        bandeja.DoubleClick += (_, _) => AbrirVentana();
        Application.Run();
    }
    catch (Exception ex) { Log("No se pudo crear el icono de la bandeja: " + ex.Message); }
}) { IsBackground = true, ApartmentState = ApartmentState.STA }.Start();
Win.Atajo(() => ordenes.Enqueue(("SALIDA 0", new TaskCompletionSource<string>())), () => Log("AVISO: no se pudo reservar el atajo Ctrl+Alt+V (lo usa otro programa)."));

while (!saliendo)
{
    try
    {
        // --- CasparCG: esta vivo si responde su puerto de control ---
        vivo = PuertoAbierto();
        if (vivo) { if (caido) Log("CasparCG responde."); caido = false; fallos = 0; }
        else if (DateTime.Now > gracia && (++fallos >= 3 || primera))
            AbrirMotor(primera ? "CasparCG no esta abierto: se abre." : "CASPARCG NO RESPONDE: se cierra y se vuelve a abrir.");
        primera = false;

        Mantener(parrilla);
        VigilarVentana();
        if (emerg.Activa) Mantener(emergProg);
        if (vivo) Vigilar();
        VigilarAudio();
        if (vivo && DateTime.Now > gracia && (DateTime.Now - ultRespuesta).TotalSeconds > 15)
            AbrirMotor("EL MOTOR ESTA PEGADO: el puerto abre pero no contesta a sus ordenes; se cierra y se vuelve a abrir.");
        if (vivo && emerg is { Activa: true, VolverPend: true }) VolverAlTerminar();
        while (ordenes.TryDequeue(out var o)) o.resp.SetResult(Ejecutar(o.cmd));
    }
    catch (Exception ex) { Log("Error del supervisor: " + ex.Message); }
    Thread.Sleep(1000);
}
if (bandeja != null) { bandeja.Visible = false; bandeja.Dispose(); }
Thread.Sleep(500); // da tiempo a que la respuesta de CERRAR llegue antes de salir

// Si el aire sale por un dispositivo fijo (por ejemplo el cable a la mesa) y no esta conectado cuando el motor arranca, el motor queda sin sonido
// (CasparCG no vuelve solo al predeterminado). Cuando el dispositivo vuelve, se reabre el motor para recuperar el sonido.
void VigilarAudio()
{
    if ((DateTime.Now - audioVisto).TotalSeconds < 5) return;
    audioVisto = DateTime.Now;
    try
    {
        var n = Comun.AudioAire(cfg);
        if (n == "") { audioFalta = false; return; }
        using var en = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        var esta = en.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active).Any(d => d.FriendlyName == n);
        if (!esta && !audioFalta) { audioFalta = true; Log($"AVISO: el dispositivo de audio del aire no esta conectado: {n}"); }
        else if (esta && audioFalta && vivo && DateTime.Now > gracia) { audioFalta = false; AbrirMotor($"El dispositivo de audio del aire volvio ({n}): se reabre el motor para recuperar el sonido."); }
        else if (esta) audioFalta = false;
    }
    catch { }
}

// Cierra el motor (si hay) y lo abre de nuevo, sin ventana de consola: solo la imagen de salida.
void AbrirMotor(string motivo)
{
    Log(motivo);
    foreach (var p in Process.GetProcessesByName("casparcg")) { try { p.Kill(true); } catch { } }
    Process.Start(new ProcessStartInfo(Path.Combine(cfg.CasparDir, "casparcg.exe")) { WorkingDirectory = cfg.CasparDir, UseShellExecute = false, CreateNoWindow = true });
    gracia = DateTime.Now.AddSeconds(20); // tiempo para arrancar antes de volver a contarlo como caido
    fallos = 0; caido = true;
    ultRespuesta = ultAvanceE = DateTime.Now;
}

void AbrirVentana()
{
    var exe = Path.Combine(AppContext.BaseDirectory, "CaLabs.Parrilla.exe");
    if (Process.GetProcessesByName("CaLabs.Parrilla").Length == 0 && File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
}

bool PuertoAbierto()
{
    try
    {
        using var c = new TcpClient();
        return c.ConnectAsync(cfg.AmcpHost, cfg.AmcpPort).Wait(700) && c.Connected;
    }
    catch { return false; }
}

// Abre el programador si no esta, y lo reinicia si lleva mas de 10 s sin dar senal de vida (congelado).
void Mantener(Hijo h)
{
    if (h.P == null || h.P.HasExited)
    {
        if (h.P != null) Log($"{h.Nombre.ToUpper()} SE CERRO (codigo {h.P.ExitCode}): se vuelve a abrir.");
        h.P = Process.Start(new ProcessStartInfo(exeProg, h.Args) { UseShellExecute = false, CreateNoWindow = true });
        h.Abierto = DateTime.Now;
        Log($"{h.Nombre} abierto (PID {h.P?.Id}).");
    }
    else if (vivo && DateTime.Now > gracia && (DateTime.Now - h.Abierto).TotalSeconds > 15
             && (DateTime.Now - File.GetLastWriteTime(h.RutaEstado)).TotalSeconds > 10)
    {
        Log($"{h.Nombre.ToUpper()} NO RESPONDE: se cierra y se vuelve a abrir.");
        Matar(h);
    }
}

// La ventana de la Parrilla no manda en el aire, pero el operador la necesita: si se cae (cierra con error) o se cuelga, se vuelve a abrir.
// Si el operador la cierra normalmente, se respeta.
void VigilarVentana()
{
    var exe = Path.Combine(AppContext.BaseDirectory, "CaLabs.Parrilla.exe");
    void Abrir() { try { ventana = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false }); if (ventana != null) hVentana = Win.OpenProcess(0x1000, false, ventana.Id); } catch (Exception ex) { Log("No se pudo abrir la ventana: " + ex.Message); } }
    if (ventana == null)
    {
        ventana = Process.GetProcessesByName("CaLabs.Parrilla").FirstOrDefault();
        if (ventana != null) hVentana = Win.OpenProcess(0x1000, false, ventana.Id); // PROCESS_QUERY_LIMITED_INFORMATION
        return;
    }
    if (ventana.HasExited)
    {
        var codigo = hVentana != IntPtr.Zero && Win.GetExitCodeProcess(hVentana, out var c) ? c : 0;
        if (hVentana != IntPtr.Zero) Win.CloseHandle(hVentana);
        hVentana = IntPtr.Zero;
        ventana = null;
        if (codigo != 0 && !saliendo && File.Exists(exe)) { Log($"LA VENTANA SE CERRO CON ERROR (codigo {codigo}): se vuelve a abrir."); Abrir(); }
        return;
    }
    ventana.Refresh(); // .NET guarda el ultimo valor de 'Responding'
    if (ventana.Responding) { colgada = DateTime.MinValue; return; }
    if (colgada == DateTime.MinValue) { colgada = DateTime.Now; return; }
    if ((DateTime.Now - colgada).TotalSeconds < 20) return;
    Log("LA VENTANA NO RESPONDE: se cierra y se vuelve a abrir.");
    try { ventana.Kill(); ventana.WaitForExit(3000); } catch { }
    colgada = DateTime.MinValue; ventana = null;
    if (hVentana != IntPtr.Zero) { Win.CloseHandle(hVentana); hVentana = IntPtr.Zero; }
    if (File.Exists(exe)) Abrir();
}

void Matar(Hijo h)
{
    try { h.P?.Kill(); h.P?.WaitForExit(2000); } catch { }
    h.P = null;
}

// Vigilante: lo unico que mira es si el video de la parrilla avanza. Un negro dentro de un video no cuenta.
void Vigilar()
{
    try
    {
        if (amcp == null)
        {
            amcp = new Amcp(cfg.AmcpHost, cfg.AmcpPort);
            ultAvance = DateTime.Now; ultPos = -1;
            if (emerg.Activa) amcp.Enviar($"MIXER {CapaP()} VOLUME 0"); // CasparCG se reinicio: volver a silenciar la parrilla
        }
        var i = amcp.Info(cfg.Canal, cfg.Capa);
        ultRespuesta = DateTime.Now;
        var pe = Comun.Leer<Estado>(parrilla.RutaEstado);
        // Con la emergencia al aire tambien se mira que su video avance: si el motor esta bloqueado, la emergencia no puede salvar nada.
        if (emerg.Activa)
        {
            var ie = amcp.Info(cfg.Canal, cfg.CapaEmergencia);
            var ee = Comun.Leer<Estado>(emergProg.RutaEstado);
            if (ie.Fg == null || ie.Pos != ultPosE || ee is { Pausa: true }) ultAvanceE = DateTime.Now;
            ultPosE = ie.Pos;
            if ((DateTime.Now - ultAvanceE).TotalSeconds > 30 && DateTime.Now > gracia) { AbrirMotor("LA EMERGENCIA TAMPOCO AVANZA: el motor esta bloqueado; se cierra y se vuelve a abrir."); return; }
        }
        else ultAvanceE = DateTime.Now;
        // detenido o en pausa a proposito: no es un atasco
        if ((i.Fg != null && i.Pos != ultPos) || pe == null || !pe.Activo || pe.Pausa) ultAvance = DateTime.Now;
        ultPos = i.Pos;
        if (pe is { Activo: true, Fin: true } && !emerg.Activa) Entrar("termino la parrilla");
        else if (!emerg.Activa && (DateTime.Now - ultAvance).TotalSeconds > cfg.AtascoSeg)
            Entrar(i.Fg == null ? "no hay nada al aire" : "el video dejo de avanzar");
    }
    catch (Exception ex) when (ex is IOException or SocketException) { amcp?.Dispose(); amcp = null; }
}

void Entrar(string motivo)
{
    var e = Comun.Leer<Estado>(parrilla.RutaEstado) ?? new Estado();
    emerg = new Emergencia { Activa = true, Motivo = motivo, Desde = DateTime.Now };
    Comun.Guardar(rutaEmerg, emerg);
    try { File.Delete(emergProg.RutaEstado); } catch { } // la emergencia siempre empieza desde su primer video
    amcp?.Enviar($"MIXER {CapaP()} VOLUME 0");           // que no se mezcle el audio de la parrilla
    Log($"EMERGENCIA AL AIRE: {motivo}. La parrilla iba en #{e.Idx + 1} {Path.GetFileName(e.Clip)} a los {e.Pos:0} s.");
    Mantener(emergProg);
}

int Largo(string lista) => Comun.Leer<Lista>(Path.Combine(raiz, lista))?.Entradas.Length ?? 0;

// Vuelta pedida "cuando termine el video": se lanza cuando al video de emergencia le quedan 2 s o menos.
void VolverAlTerminar()
{
    try
    {
        if (amcp == null) return;
        var i = amcp.Info(cfg.Canal, cfg.CapaEmergencia);
        if (i.Fg == null || i.Final <= 0 || i.Final - i.Pos > 2) return;
        var r = Volver(emerg.VolverIdx);
        if (!r.StartsWith('2')) { emerg.VolverPend = false; Comun.Guardar(rutaEmerg, emerg); Log("No se pudo volver al terminar el video: " + r); }
    }
    catch (Exception ex) when (ex is IOException or SocketException) { amcp?.Dispose(); amcp = null; }
}

// La vuelta es manual y siempre desde el elemento pedido (0 = el principio de la parrilla).
string Volver(int idx)
{
    if (!emerg.Activa) return "400 No hay emergencia activa";
    if (amcp == null) return "500 CasparCG no responde";
    var n = Largo(cfg.Lista);
    if (n == 0) return "400 La parrilla no tiene videos";
    Matar(parrilla);
    amcp.Enviar($"CLEAR {CapaP()}");
    Comun.Guardar(parrilla.RutaEstado, new Estado { Activo = true, Idx = Math.Clamp(idx, 0, n - 1) });
    Mantener(parrilla);
    for (var k = 0; k < 16; k++) // hasta 8 s para que la parrilla salga; mientras tanto sigue la emergencia
    {
        Thread.Sleep(500);
        var i = amcp.Info(cfg.Canal, cfg.Capa);
        if (i.Fg == null || i.Pos <= 0) continue;
        emerg = new Emergencia(); Comun.Guardar(rutaEmerg, emerg);
        Matar(emergProg);
        amcp.Enviar($"CLEAR {CapaE()}");
        amcp.Enviar($"MIXER {CapaP()} VOLUME 1");
        ultAvance = DateTime.Now;
        Log($"FIN DE LA EMERGENCIA: vuelve la parrilla desde #{Math.Clamp(idx, 0, n - 1) + 1}.");
        return "200 OK";
    }
    return "500 La parrilla no pudo salir: sigue la emergencia";
}

string Ejecutar(string cmd)
{
    if (cmd.StartsWith("AUDIOAIRE", StringComparison.OrdinalIgnoreCase)) return AudioAire(cmd.Length > 9 ? cmd[9..].Trim() : ""); // el nombre del dispositivo distingue mayusculas
    var c = cmd.Trim().ToUpperInvariant();
    var n = int.TryParse(c.Split(' ')[^1], out var v) ? v : 0; // ultimo dato de la orden, si es un numero
    if (c == "ESTADO") return JsonSerializer.Serialize(new { caspar = vivo, emergencia = emerg, alAire = Comun.Leer<Estado>(parrilla.RutaEstado) });
    if (c == "EMERGENCIA")
    {
        if (emerg.Activa) return "400 La emergencia ya esta activa";
        if (Largo(cfg.ListaEmergencia) == 0) return "400 La playlist de emergencia no tiene videos";
        Entrar("manual"); return "200 OK";
    }
    if (c.StartsWith("VOLVER AL TERMINAR"))
    {
        if (!emerg.Activa) return "400 No hay emergencia activa";
        if (Largo(cfg.Lista) == 0) return "400 La parrilla no tiene videos";
        emerg.VolverPend = true; emerg.VolverIdx = n; Comun.Guardar(rutaEmerg, emerg);
        Log($"Volvera a la parrilla (#{n + 1}) cuando termine el video de emergencia.");
        return "200 OK";
    }
    if (c.StartsWith("VOLVER")) return Volver(n);
    if (c.StartsWith("SALIDA")) return Salida(n);
    if (c == "ABRIR") // vuelve a abrir la ventana de salida: reinicia el motor (unos segundos sin señal)
    {
        AbrirMotor("Se reabre la salida al aire (orden de la pantalla). Se reinicia el motor.");
        return "200 OK";
    }
    if (c == "CERRAR") // apaga todo el playout (el canal se queda sin señal)
    {
        Log("CIERRE DE TODO EL PLAYOUT (orden de la pantalla).");
        saliendo = true; Matar(parrilla); Matar(emergProg);
        foreach (var p in Process.GetProcessesByName("casparcg")) { try { p.Kill(true); } catch { } }
        return "200 OK";
    }
    return "400 Orden desconocida";
}

// Por donde suena el aire: el motor usa el dispositivo de audio que dice su configuracion ("" = el predeterminado de Windows). Reinicia el motor.
string AudioAire(string nombre)
{
    var ruta = Path.Combine(cfg.CasparDir, "casparcg.config");
    var x = XDocument.Load(ruta);
    var s = x.Descendants("system-audio").FirstOrDefault();
    if (s == null) return "500 La configuracion de CasparCG no tiene salida de audio";
    s.ReplaceNodes(nombre == "" ? [] : new object[] { new XElement("device-name", "OpenAL Soft on " + nombre) });
    x.Save(ruta);
    AbrirMotor(nombre == "" ? "Audio al aire por el dispositivo predeterminado de Windows. Se reinicia el motor." : $"Audio al aire por «{nombre}». Se reinicia el motor.");
    return "200 OK";
}

// Donde se ve el aire: 0 = ventana pequena (pruebas), 1.. = ese monitor a pantalla completa.
// Se guarda en la configuracion de CasparCG y se reinicia el motor para aplicarlo (unos segundos sin senal).
string Salida(int n)
{
    var ruta = Path.Combine(cfg.CasparDir, "casparcg.config");
    var x = XDocument.Load(ruta);
    var s = x.Descendants("screen").FirstOrDefault();
    if (s == null) return "500 La configuracion de CasparCG no tiene salida de pantalla";
    if (n <= 0 && (string?)s.Element("borderless") != "true") return "200 OK"; // ya esta en ventana: no se corta el aire
    var (an, al, x0, y0) = n <= 0 ? (960, 540, 0, 0) : Comun.Pantalla(n);
    if (an == 0) return $"400 La pantalla {n} no existe o esta apagada";
    // Una ventana sin borde del tamano y en el lugar exactos de la pantalla: el modo "pantalla completa" de CasparCG usa el tamano
    // de la pantalla principal en todas, y su numero de pantalla no siempre coincide con el de Windows.
    // CasparCG cuenta la posicion desde su primera pantalla (la 1 de Windows).
    var (_, _, x1, y1) = Comun.Pantalla(1);
    s.ReplaceNodes(n <= 0
        ? [new XElement("stretch", "uniform"), new XElement("windowed", "true"), new XElement("width", an), new XElement("height", al)]
        : new object[] { new XElement("stretch", "uniform"), new XElement("windowed", "true"), new XElement("borderless", "true"), new XElement("always-on-top", "true"),
                         new XElement("x", x0 - x1), new XElement("y", y0 - y1), new XElement("width", an), new XElement("height", al) });
    x.Save(ruta);
    Log(n <= 0 ? "Salida al aire: ventana. Se reinicia el motor." : $"Salida al aire: monitor {n} a pantalla completa. Se reinicia el motor.");
    foreach (var p in Process.GetProcessesByName("casparcg")) { try { p.Kill(true); } catch { } }
    gracia = DateTime.MinValue; fallos = 2; // se reabre en la vuelta siguiente
    return "200 OK";
}

// Puerto de ordenes, solo accesible desde este mismo PC. Las ordenes las ejecuta el bucle principal.
void Control()
{
    try
    {
        var l = new TcpListener(IPAddress.Loopback, cfg.PuertoControl);
        l.Start();
        while (true)
        {
            var c = l.AcceptTcpClient();
            new Thread(() =>
            {
                try
                {
                    using (c)
                    {
                        var s = c.GetStream();
                        var r = new StreamReader(s);
                        var w = new StreamWriter(s) { AutoFlush = true, NewLine = "\r\n" };
                        string? x;
                        while ((x = r.ReadLine()) != null)
                        {
                            var t = new TaskCompletionSource<string>();
                            ordenes.Enqueue((x, t));
                            w.WriteLine(t.Task.Result);
                        }
                    }
                }
                catch { }
            }) { IsBackground = true }.Start();
        }
    }
    catch (Exception ex) { Log("No se pudo abrir el puerto de ordenes: " + ex.Message); }
}

static class Win
{
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint acceso, bool heredar, int pid);
    [DllImport("kernel32.dll")] public static extern bool GetExitCodeProcess(IntPtr h, out int codigo);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint tecla);
    [DllImport("user32.dll")] static extern int GetMessageW(out Msg m, IntPtr h, uint min, uint max);
    struct Msg { public IntPtr H; public uint M; public IntPtr W, L; public uint T; public int X, Y; }

    // Ctrl+Alt+V en cualquier parte de Windows: la salida vuelve a ventana. Es la salida de escape si la pantalla completa tapa la Parrilla.
    public static void Atajo(Action alPulsar, Action siFalla) => new Thread(() =>
    {
        if (!RegisterHotKey(IntPtr.Zero, 1, 0x0001 | 0x0002, 0x56)) { siFalla(); return; } // Alt | Ctrl, tecla V
        while (GetMessageW(out var m, IntPtr.Zero, 0, 0) > 0) if (m.M == 0x0312) alPulsar(); // WM_HOTKEY
    }) { IsBackground = true }.Start();
}

class Hijo(string nombre, string args, string rutaEstado)
{
    public string Nombre = nombre, Args = args, RutaEstado = rutaEstado;
    public Process? P;
    public DateTime Abierto;
}
