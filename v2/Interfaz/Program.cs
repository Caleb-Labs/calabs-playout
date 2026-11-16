// CaLabs Playout 2.0 - ventana Parrilla: una ventana nativa que muestra la pantalla (carpeta ui).
// No reproduce nada: si se cierra o se cuelga, el aire sigue. Solo muestra y deja ordenes en archivos.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CaLabs;

class Biblioteca
{
    public List<string> Carpetas { get; set; } = []; // de la version con carpetas vinculadas: se pasan a Sueltos y se vacia
    public List<string> Sueltos { get; set; } = [];  // cada video con su ruta completa, este en el disco que este
}

static class Parrilla
{
    static string raiz = "";
    static Config cfg = new();
    static Window win = null!;
    static WebView2 wv = null!;
    static Biblioteca bib = new();
    static Dictionary<string, double> durs = [];
    static readonly object candado = new(), turno = new();
    static readonly HashSet<string> EXT = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".m4v", ".webm", ".mov", ".ogv", ".mkv", ".mpg", ".mpeg", ".avi", ".wmv", ".flv", ".ts", ".vob", ".mts", ".m2ts", ".m2v", ".mxf" };

    static bool perfilNuevo;
    static readonly object cGuardados = new();

    // Copia en archivo de todo lo que la pantalla guarda (tandas, plantillas, parrilla...). Si se borra la memoria interna, se recupera de aqui.
    static void GuardarCopia(string json)
    {
        lock (cGuardados)
        {
            JsonDocument.Parse(json).Dispose(); // se valida antes de escribir
            var f = Datos("guardados.json");
            if (File.Exists(f))
            {
                var dir = Datos("respaldos"); Directory.CreateDirectory(dir);
                var ult = Directory.GetFiles(dir, "guardados-*.json").OrderBy(x => x).LastOrDefault();
                if (ult == null || (DateTime.Now - File.GetLastWriteTime(ult)).TotalMinutes >= 10) File.Copy(f, Path.Combine(dir, $"guardados-{DateTime.Now:yyyyMMdd-HHmmss}.json"));
                foreach (var v in Directory.GetFiles(dir, "guardados-*.json").OrderByDescending(x => x).Skip(30)) { try { File.Delete(v); } catch { } }
            }
            File.WriteAllText(f + ".tmp", json); File.Move(f + ".tmp", f, true);
        }
    }

    static string Datos(string f) => Path.Combine(raiz, "datos", f);
    static void Log(string m) => Comun.Log(raiz, "Parrilla", m);
    static void Post(string json) => win.Dispatcher.BeginInvoke(() => { try { wv.CoreWebView2.PostWebMessageAsJson(json); } catch { } });

    [STAThread]
    static void Main(string[] args)
    {
        raiz = Comun.Raiz();
        cfg = Comun.LeerConfig(raiz);
        bib = Comun.Leer<Biblioteca>(Datos("biblioteca.json")) ?? new Biblioteca();
        durs = Comun.Leer<Dictionary<string, double>>(Datos("duraciones.json")) ?? [];
        perfilNuevo = !Directory.Exists(Datos(Path.Combine("webview", "EBWebView"))); // memoria interna recien creada o borrada
        var app = new Application();
        wv = new WebView2();
        win = new Window { Title = "CaLabs Playout", Width = 1500, Height = 900, Background = Brushes.Black, Content = wv };
        win.Loaded += async (_, _) =>
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, Datos("webview"));
                await wv.EnsureCoreWebView2Async(env);
                var s = wv.CoreWebView2.Settings;
                s.AreDefaultContextMenusEnabled = s.AreDevToolsEnabled = s.IsStatusBarEnabled = false;
                s.AreBrowserAcceleratorKeysEnabled = s.IsZoomControlEnabled = false; // que no se comporte como un navegador
                wv.CoreWebView2.SetVirtualHostNameToFolderMapping("calabs.app", Path.Combine(raiz, "ui"), CoreWebView2HostResourceAccessKind.Allow);
                wv.CoreWebView2.WebMessageReceived += (_, ev) =>
                {
                    try { Recibir(ev); } catch (Exception ex) { Log("Orden de la pantalla con error: " + ex.Message); }
                };
                wv.Source = new Uri("https://calabs.app/playout/index.html");
                new Thread(Previa) { IsBackground = true }.Start();
                new Thread(Estado) { IsBackground = true }.Start();
                new Thread(VigiaSupervisor) { IsBackground = true }.Start();
                new Thread(Niveles) { IsBackground = true }.Start();
                new Thread(VigiaAudio) { IsBackground = true }.Start();
                win.Closed += (_, _) => Audio.Detener();
                if (args.Contains("--prueba")) new Thread(Prueba) { IsBackground = true }.Start();
            }
            catch (Exception ex) { Log("No se pudo abrir la pantalla: " + ex.Message); MessageBox.Show(ex.Message, "CaLabs Playout"); }
        };
        app.Run(win);
    }

    // ---------- ordenes de la pantalla ----------
    static void Recibir(CoreWebView2WebMessageReceivedEventArgs ev)
    {
        using var doc = JsonDocument.Parse(ev.WebMessageAsJson);
        var r = doc.RootElement;
        string T(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        switch (T("t"))
        {
            case "lista": // se valida antes de escribir: es lo que el Programador va a emitir
                var l = JsonSerializer.Deserialize<Lista>(T("json"), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var destino = Path.Combine(raiz, T("cual") == "emergencia" ? cfg.ListaEmergencia : cfg.Lista);
                // si no cambio no se reescribe: el Programador no tiene que recargarla en pleno aire
                if (l != null && !(File.Exists(destino) && File.ReadAllText(destino) == Comun.Texto(l))) Comun.Guardar(destino, l);
                break;
            case "mando":
                Comun.Guardar(Datos(T("cual") == "emergencia" ? "mando-emergencia.json" : "mando.json"), new Mando
                {
                    N = DateTime.UtcNow.Ticks, Cmd = T("cmd"), Nombre = T("nombre"), Modo = T("modo"),
                    Clips = r.TryGetProperty("clips", out var cl) && cl.ValueKind == JsonValueKind.Array ? [.. cl.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "")] : [],
                    Arg = r.TryGetProperty("arg", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble() : 0
                });
                break;
            case "orden": // al supervisor (emergencia y volver); puede tardar: fuera del hilo de la ventana
                var cmd = T("cmd");
                Task.Run(() =>
                {
                    var rr = Orden(cmd);
                    Post(Comun.Texto(new { t = "respuesta", cmd, r = rr }));
                    if (cmd == "CERRAR" && rr.StartsWith('2')) win.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
                });
                break;
            case "log": Log("Pantalla: " + T("m")); break;
            case "volumen": // volumen de la salida: lo aplica el Programador
                Comun.Guardar(Datos("audio.json"), new AudioCfg { Vol = Math.Clamp(r.TryGetProperty("v", out var vv) && vv.ValueKind == JsonValueKind.Number ? vv.GetDouble() : 1, 0, 1) });
                break;
            case "grafica": // configuracion de logo, reloj, zocalo y ticker: la lee el Programador
                JsonDocument.Parse(T("json")).Dispose(); // se valida antes de escribir
                if (!(File.Exists(Datos("grafica.json")) && File.ReadAllText(Datos("grafica.json")) == T("json"))) { File.WriteAllText(Datos("grafica.json") + ".tmp", T("json")); File.Move(Datos("grafica.json") + ".tmp", Datos("grafica.json"), true); }
                break;
            case "evento": // zocalo o ticker manual: va directo a la pagina de graficas del motor
                var fn = T("fn"); var js = T("json");
                if (fn is "zoc" or "tk" or "tkOff" or "fx") Task.Run(() =>
                {
                    try
                    {
                        using var a = new Amcp(cfg.AmcpHost, cfg.AmcpPort);
                        a.Enviar($"CALL {cfg.Canal}-{cfg.CapaGrafica} \"{fn}('{Convert.ToBase64String(Encoding.UTF8.GetBytes(js))}')\"");
                    }
                    catch { } // sin motor no hay nada que mostrar
                });
                break;
            case "logo": // el logo elegido por el operador se copia a datos para que el motor lo lea
                var lf = ev.AdditionalObjects.OfType<CoreWebView2File>().FirstOrDefault();
                if (lf != null && File.Exists(lf.Path))
                {
                    foreach (var v in Directory.GetFiles(Datos(""), "logo.*")) File.Delete(v);
                    var nom = "logo" + Path.GetExtension(lf.Path).ToLowerInvariant();
                    File.Copy(lf.Path, Datos(nom), true);
                    Post(Comun.Texto(new { t = "logo", ruta = "datos/" + nom }));
                }
                break;
            case "listo":
                salidaVista = -1; ultAudio = ""; Biblio("");
                lock (cGuardados) Post(Comun.Texto(new { t = "guardados", nuevo = perfilNuevo, json = File.Exists(Datos("guardados.json")) ? File.ReadAllText(Datos("guardados.json")) : "" }));
                break;
            case "guardados": GuardarCopia(T("json")); break;
            case "audioLista": Task.Run(() => Post(Comun.Texto(new { t = "audioLista", salidas = Audio.Salidas(), aire = AudioAireActual() }))); break;
            case "audioAire": { var nom = T("nombre"); Task.Run(() => Post(Comun.Texto(new { t = "respuesta", cmd = "AUDIOAIRE", r = Orden("AUDIOAIRE " + nom) }))); break; }
            case "escucha": // Mi escucha: encender, apagar o cambiar el volumen
            {
                var on = r.TryGetProperty("on", out var o1) && o1.ValueKind == JsonValueKind.True;
                var vol = r.TryGetProperty("vol", out var v1) && v1.ValueKind == JsonValueKind.Number ? (float)v1.GetDouble() : 0.6f;
                var dest = T("destino"); var orig = T("origen");
                var soloVolumen = r.TryGetProperty("soloVolumen", out var sv) && sv.ValueKind == JsonValueKind.True;
                if (soloVolumen) { Audio.Volumen(vol); break; }
                Task.Run(() =>
                {
                    var fallo = on ? Audio.Escuchar(orig, dest, vol) : ""; if (!on) Audio.Detener();
                    Post(Comun.Texto(new { t = "escuchaEstado", ok = fallo == "", msg = fallo }));
                    if (on && fallo == "") { Thread.Sleep(4000); Log($"Mi escucha activa: {Audio.Bytes / 1024} KB de audio capturados en 4 s."); }
                });
                break;
            }
            case "negros": // la pantalla pide revisar si estos videos terminan en negro y sin sonido
                var pedidas = r.GetProperty("rutas").EnumerateArray().Select(x => x.GetString() ?? "").Where(File.Exists).ToArray();
                Task.Run(() => Negros(pedidas));
                break;
            case "carpeta": // agrega de una vez todos los videos de una carpeta; no queda "vinculada"
                var d = new Microsoft.Win32.OpenFolderDialog { Title = "Elige la carpeta de videos" };
                if (d.ShowDialog(win) == true) { Sumar([d.FolderName]); Biblio("carpeta"); }
                break;
            case "archivos": // elegidos o soltados desde Windows (archivos o carpetas, de cualquier disco)
                var nuevos = Sumar(ev.AdditionalObjects.OfType<CoreWebView2File>().Select(f => f.Path));
                if (r.TryGetProperty("soltar", out _)) Post(Comun.Texto(new { t = "soltados", nombres = nuevos.Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase) }));
                Biblio("archivos");
                break;
            case "quitar":
                lock (candado) { bib.Sueltos.RemoveAll(p => Path.GetFileName(p).Equals(T("nombre"), StringComparison.OrdinalIgnoreCase)); Comun.Guardar(Datos("biblioteca.json"), bib); }
                break;
            case "limpiar":
                lock (candado) { bib.Sueltos.Clear(); Comun.Guardar(Datos("biblioteca.json"), bib); }
                break;
        }
    }

    // Nivel de audio: CasparCG lo manda por OSC (UDP) con el pico de cada canal; la pantalla lo pinta en el vumetro unas 15 veces por segundo.
    static void Niveles()
    {
        try
        {
            using var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, cfg.PuertoOsc));
            var ep = new IPEndPoint(IPAddress.Any, 0); var ult = DateTime.MinValue;
            while (true)
            {
                var b = u.Receive(ref ep);
                if ((DateTime.Now - ult).TotalMilliseconds < 60) continue;
                if (Pico(b, 0, b.Length) is { } p) { ult = DateTime.Now; Post(Comun.Texto(new { t = "vu", l = Db(p.l), r = Db(p.r) })); }
            }
        }
        catch (Exception ex) { Log("El vumetro no esta disponible: " + ex.Message); }
    }
    static double Db(int v) => v <= 0 ? -90 : Math.Max(-90, 20 * Math.Log10(v / 2147483648.0));

    // Busca "/channel/N/mixer/audio/volume" en un paquete OSC (mensaje suelto o "bundle" con varios) y devuelve el pico izquierdo y derecho.
    static (int l, int r)? Pico(byte[] b, int ini, int fin)
    {
        if (fin - ini > 16 && Encoding.ASCII.GetString(b, ini, 7) == "#bundle")
        {
            for (var i = ini + 16; i + 4 <= fin;)
            {
                var n = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(i)); i += 4;
                if (n <= 0 || i + n > fin) break;
                if (Pico(b, i, i + n) is { } p) return p;
                i += n;
            }
            return null;
        }
        var j = Array.IndexOf(b, (byte)0, ini); if (j < 0 || j >= fin) return null;
        if (Encoding.ASCII.GetString(b, ini, j - ini) != $"/channel/{cfg.Canal}/mixer/audio/volume") return null;
        var k = ini + ((j - ini + 4) & ~3); var z = Array.IndexOf(b, (byte)0, k); if (z < 0 || z >= fin) return null; // OSC alinea a 4 bytes desde el inicio del mensaje
        var datos = ini + ((z - ini + 4) & ~3); // despues de la lista de tipos (",iiii...") vienen los valores de 4 bytes
        return datos + 8 <= fin ? (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(datos)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(datos + 4))) : null;
    }

    // El supervisor es lo que mantiene vivo el motor: si no esta abierto (se cerro, se cayo), esta ventana lo vuelve a abrir.
    static void VigiaSupervisor()
    {
        var fallos = 0;
        while (true)
        {
            Thread.Sleep(5000);
            try { using var c = new TcpClient(); if (c.ConnectAsync(IPAddress.Loopback, cfg.PuertoControl).Wait(1500) && c.Connected) { fallos = 0; continue; } } catch { }
            if (++fallos < 2) continue; // dos avisos seguidos: no es un reinicio normal
            fallos = 0;
            var exe = Path.Combine(AppContext.BaseDirectory, "CaLabs.Supervisor.exe");
            if (!File.Exists(exe)) continue;
            Log("El supervisor no estaba abierto: se vuelve a abrir.");
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true }); } catch (Exception ex) { Log("No se pudo abrir el supervisor: " + ex.Message); }
        }
    }

    static string Orden(string cmd)
    {
        try
        {
            using var c = new TcpClient();
            c.Connect(IPAddress.Loopback, cfg.PuertoControl);
            c.ReceiveTimeout = 30000;
            var s = c.GetStream();
            var w = new StreamWriter(s) { AutoFlush = true, NewLine = "\r\n" };
            w.WriteLine(cmd);
            return new StreamReader(s).ReadLine() ?? "500 El supervisor no respondio";
        }
        catch (Exception ex) { return "500 El supervisor no responde (" + ex.Message + ")"; }
    }

    // ---------- negro al final: se revisan los ultimos 30 s con ffmpeg; si hay negro Y silencio hasta el final, el programa termina donde empieza ----------
    static readonly object turnoNegros = new();
    static void Negros(string[] rutas) // el resultado se recuerda en datos\negros.json
    {
        try
        {
            lock (turnoNegros)
            {
                var f = Datos("negros.json");
                var cache = Comun.Leer<Dictionary<string, double>>(f) ?? [];
                var res = new Dictionary<string, double>();
                foreach (var p in rutas)
                {
                    var fi = new FileInfo(p); var k = $"{p}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
                    if (!cache.TryGetValue(k, out var fin)) { fin = FinSinNegro(p); cache[k] = fin; Comun.Guardar(f, cache); if (fin > 0) Log($"Negro al final de {Path.GetFileName(p)}: termina en {fin:0.0} s."); }
                    res[p.Replace('\\', '/')] = fin;
                }
                Post(Comun.Texto(new { t = "negros", datos = res }));
            }
        }
        catch (Exception ex) { Log("Error al revisar el negro al final: " + ex.Message); }
    }

    // Segundo del video donde empieza un final en negro y sin sonido; 0 si no lo hay (o si el video es corto).
    static double FinSinNegro(string ruta)
    {
        var dur = Medir(ruta);
        if (dur < 20) return 0; // spots y videos cortos no se revisan
        var ini = Math.Max(0, dur - 30);
        double? Ult(string salida, string patron, string ficha) // inicio del ultimo tramo que llega hasta el final
        {
            double? r = null;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(salida, patron))
                if (double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) >= dur - ini - 0.7) r = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return r;
        }
        string Ff(string filtros)
        {
            var psi = new ProcessStartInfo(Path.Combine(cfg.CasparDir, "ffmpeg.exe")) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-hide_banner", "-nostats", "-ss", ini.ToString("0.###", CultureInfo.InvariantCulture), "-i", ruta }.Concat(filtros.Split(' ')).Concat(["-f", "null", "-"])) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var s = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(60000)) { p.Kill(); return ""; }
            return s;
        }
        var negro = Ult(Ff("-an -vf blackdetect=d=0.4:pix_th=0.10"), @"black_start:([\d.]+)\s+black_end:([\d.]+)", "negro");
        if (negro == null) return 0;
        var sil = Ff("-vn -af silencedetect=n=-50dB:d=0.4");
        // sin pista de audio no hay sonido: cuenta como silencio. Con audio, el silencio tiene que llegar tambien hasta el final.
        double? silencio = sil.Contains("silence_") ? Ult(sil, @"silence_start:\s*([\d.]+)\s*\r?\n.*?silence_end:\s*([\d.]+)", "silencio") : (sil.Contains("Output file does not contain any stream") || sil.Contains("matches no streams") ? 0 : null);
        if (silencio == null) return 0;
        var fin = ini + Math.Max(negro.Value, silencio.Value);
        return fin < dur - 0.5 ? fin : 0;
    }

    static readonly EnumerationOptions Rec = new() { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true };
    static bool EsVideo(string p) => EXT.Contains(Path.GetExtension(p)) && !Path.GetFileName(p).StartsWith("._");

    // Agrega a la biblioteca los videos de esas rutas (una carpeta aporta todos los que tiene dentro). Devuelve los videos encontrados.
    static List<string> Sumar(IEnumerable<string> rutas)
    {
        var o = new List<string>();
        foreach (var p in rutas)
            try { o.AddRange(Directory.Exists(p) ? Directory.EnumerateFiles(p, "*", Rec).Where(EsVideo) : File.Exists(p) && EsVideo(p) ? [p] : []); }
            catch (Exception ex) { Log($"No se pudo leer {p}: {ex.Message}"); }
        lock (candado) foreach (var p in o) if (!bib.Sueltos.Contains(p, StringComparer.OrdinalIgnoreCase)) bib.Sueltos.Add(p);
        return o;
    }

    // 0 = ventana, 1.. = monitor a pantalla completa (lo que dice la configuracion de CasparCG)
    static int SalidaActual()
    {
        try
        {
            var s = System.Xml.Linq.XDocument.Load(Path.Combine(cfg.CasparDir, "casparcg.config")).Descendants("screen").First();
            if ((string?)s.Element("borderless") != "true") return 0;
            var (_, _, x1, y1) = Comun.Pantalla(1); // la pantalla se reconoce por su lugar y tamano
            return Enumerable.Range(1, 8).FirstOrDefault(k => Comun.Pantalla(k) == ((int)s.Element("width")!, (int)s.Element("height")!, (int)s.Element("x")! + x1, (int)s.Element("y")! + y1), 1);
        }
        catch { return 0; }
    }
    static int salidaVista = -1;

    // Dispositivo por donde sale el aire, segun la configuracion del motor ("" = el predeterminado de Windows).
    static string AudioAireActual() => Comun.AudioAire(cfg);

    // Avisa a la pantalla por donde sale el aire y si ese dispositivo no esta conectado.
    static string ultAudio = "";
    static void VigiaAudio()
    {
        while (true)
        {
            try
            {
                var n = AudioAireActual(); var falta = n != "" && !Audio.Salidas().Contains(n);
                var k = n + "|" + falta;
                if (k != ultAudio) { ultAudio = k; Post(Comun.Texto(new { t = "audioEstado", aire = n, falta })); }
            }
            catch { }
            Thread.Sleep(3000);
        }
    }

    // Avisa a la pantalla donde esta saliendo el aire y que pantallas hay conectadas (puede cambiar por el atajo Ctrl+Alt+V).
    static void AvisarSalida()
    {
        var n = SalidaActual();
        if (n == salidaVista) return;
        salidaVista = n;
        Post(Comun.Texto(new { t = "salida", n, pantallas = Enumerable.Range(1, 8).Select(k => new { n = k, t = Comun.Pantalla(k) }).Where(x => x.t.an > 0).Select(x => new { x.n, x.t.an, x.t.al }) }));
    }

    // ---------- biblioteca: cada video con su ruta completa y su duracion ----------
    static void Biblio(string motivo) => Task.Run(() =>
    {
        try
        {
            lock (turno) // una lectura a la vez; 'candado' solo se toma un instante para no frenar la ventana mientras se miden duraciones
            {
                string[] sueltos;
                lock (candado)
                {
                    if (bib.Carpetas.Count > 0) { var c = bib.Carpetas.ToArray(); bib.Carpetas.Clear(); Sumar(c); }
                    Comun.Guardar(Datos("biblioteca.json"), bib);
                    sueltos = [.. bib.Sueltos];
                }
                var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var arch = new List<(string nombre, string ruta, string clave)>();
                var repetidos = new List<string>();
                foreach (var p in sueltos)
                {
                    if (!File.Exists(p)) continue; // disco desconectado o archivo movido: la pantalla lo marca como faltante
                    var n = Path.GetFileName(p);
                    // shortcut: la pantalla identifica los videos por nombre; dos con el mismo nombre en discos distintos: vale el primero y se avisa. Pasar a identificar por ruta si el canal los tiene.
                    if (!vistos.Add(n)) { repetidos.Add(p); continue; }
                    var fi = new FileInfo(p);
                    arch.Add((n, p.Replace('\\', '/'), $"{p}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}"));
                }

                void Enviar() => Post(Comun.Texto(new
                {
                    t = "lib", motivo, repetidos,
                    archivos = arch.Select(x => new { x.nombre, x.ruta, dur = durs.GetValueOrDefault(x.clave) })
                }));
                var faltan = arch.Where(x => !durs.ContainsKey(x.clave)).ToList();
                if (faltan.Count == 0) { Enviar(); return; }
                Enviar(); // primero lo que ya se sabe; las duraciones nuevas llegan al terminar de medir
                var nuevas = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();
                Parallel.ForEach(faltan, new ParallelOptions { MaxDegreeOfParallelism = 4 }, x => nuevas[x.clave] = Medir(x.ruta));
                foreach (var (k, v) in nuevas) if (v > 0) durs[k] = v;
                Comun.Guardar(Datos("duraciones.json"), durs);
                Enviar();
            }
        }
        catch (Exception ex) { Log("Error al leer la biblioteca: " + ex.Message); }
    });

    static double Medir(string ruta)
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(cfg.CasparDir, "ffprobe.exe")) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", ruta }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var s = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(10000)) { p.Kill(); return 0; }
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
        catch { return 0; }
    }

    // ---------- estado del motor hacia la pantalla, 4 veces por segundo ----------
    static void Estado()
    {
        Amcp? a = null;
        var e = new Estado();
        var em = new Emergencia();
        for (var vuelta = 0; ; vuelta++)
        {
            try
            {
                if (vuelta % 8 == 0) AvisarSalida();
                // si el archivo se esta reemplazando justo ahora y no se puede leer, vale el ultimo valor
                e = Comun.Leer<Estado>(Datos("estado.json")) ?? (File.Exists(Datos("estado.json")) ? e : new Estado());
                em = Comun.Leer<Emergencia>(Datos("emergencia-estado.json")) ?? (File.Exists(Datos("emergencia-estado.json")) ? em : new Emergencia());
                var v = em.Activa ? Comun.Leer<Estado>(Datos("estado-emergencia.json")) ?? new Estado() : e; // lo que se esta viendo
                a ??= new Amcp(cfg.AmcpHost, cfg.AmcpPort);
                var i = a.Info(cfg.Canal, em.Activa ? cfg.CapaEmergencia : cfg.Capa);
                Post(Comun.Texto(new { t = "estado", motor = true, e.Activo, v.Pausa, v.Id, fg = i.Fg, pos = i.Pos, dur = i.Dur, emerg = em, e.Pend, e.Tanda, e.TandaIdx, e.TandaDe, e.TandaAncla }));
                Thread.Sleep(250);
            }
            catch
            {
                a?.Dispose(); a = null;
                Post(Comun.Texto(new { t = "estado", motor = false }));
                Thread.Sleep(1500);
            }
        }
    }

    // Solo con --prueba (desarrollo): ejecuta en la pantalla el archivo datos/prueba.js cada vez que cambia.
    static void Prueba()
    {
        var t = DateTime.MinValue;
        while (true)
        {
            try
            {
                var f = Datos("prueba.js");
                if (File.Exists(f) && File.GetLastWriteTimeUtc(f) != t)
                {
                    t = File.GetLastWriteTimeUtc(f);
                    var js = File.ReadAllText(f);
                    win.Dispatcher.BeginInvoke(() => wv.CoreWebView2.ExecuteScriptAsync(js));
                }
            }
            catch { }
            Thread.Sleep(300);
        }
    }

    // ---------- vista previa: copia pequena de la salida (MJPEG por TCP local) ----------
    // Si la conexion se corta (CasparCG se reinicio), se vuelve a pedir.
    static void Previa()
    {
        var url = $"tcp://127.0.0.1:{cfg.PuertoPrevia}";
        TcpListener l;
        try { l = new TcpListener(IPAddress.Loopback, cfg.PuertoPrevia); l.Start(); }
        catch (Exception ex) { Log("Vista previa no disponible: " + ex.Message); return; }
        while (true)
        {
            try
            {
                using (var a = new Amcp(cfg.AmcpHost, cfg.AmcpPort))
                {
                    a.Enviar($"REMOVE {cfg.Canal} STREAM {url}");
                    a.Enviar($"ADD {cfg.Canal} STREAM {url} -format mpjpeg -codec:v mjpeg -filter:v fps=15,scale=480:270:out_range=pc,format=yuvj420p");
                }
                for (var n = 0; n < 50 && !l.Pending(); n++) Thread.Sleep(100);
                if (l.Pending())
                {
                    using var c = l.AcceptTcpClient();
                    using var s = new BufferedStream(c.GetStream());
                    c.ReceiveTimeout = 5000;
                    while (true) // cada parte: cabeceras de texto, linea vacia y el JPEG
                    {
                        var largo = -1;
                        for (string h; (h = Linea(s)).Length > 0 || largo < 0;)
                            if (h.StartsWith("Content-length:", StringComparison.OrdinalIgnoreCase)) largo = int.Parse(h[15..].Trim());
                        if (largo <= 0 || largo > 2_000_000) throw new IOException("cuadro invalido");
                        var jpg = new byte[largo];
                        s.ReadExactly(jpg);
                        var txt = "data:image/jpeg;base64," + Convert.ToBase64String(jpg);
                        win.Dispatcher.BeginInvoke(() => { try { wv.CoreWebView2.PostWebMessageAsString(txt); } catch { } });
                    }
                }
            }
            catch { } // CasparCG cerrado o conexion cortada: se reintenta
            Thread.Sleep(2000);
        }
    }

    static string Linea(Stream s)
    {
        var sb = new StringBuilder();
        for (int b; (b = s.ReadByte()) != '\n';)
        {
            if (b < 0) throw new IOException("fin de la vista previa");
            if (b != '\r') sb.Append((char)b);
        }
        return sb.ToString();
    }
}
