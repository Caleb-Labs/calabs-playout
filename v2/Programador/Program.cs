// CaLabs Playout 2.0 - Programador: sin ventana. Lleva la cuenta de que tramo va al aire
// y le adelanta el siguiente a CasparCG (LOADBG ... AUTO), para que el cambio lo haga el motor
// aunque este programa o la pantalla se caigan. La pantalla solo le deja la lista y ordenes en archivos.
using System.Globalization;
using CaLabs;

var raiz = Comun.Raiz();
if (args.Contains("--prueba")) { Logica.Probar(); Grafica.Probar(); Console.WriteLine("Pruebas OK"); return; }

var cfg = Comun.LeerConfig(raiz);
string Arg(string n, string def) { var k = Array.IndexOf(args, "--" + n); return k >= 0 && k + 1 < args.Length ? args[k + 1] : def; }
var nombre = Arg("nombre", "Programador");
var capa = int.Parse(Arg("capa", cfg.Capa.ToString()));
var siempre = args.Contains("--siempre"); // la emergencia: siempre activa; no acepta "aire" ni "parar"
var rutaEstado = Path.Combine(raiz, "datos", Arg("estado", "estado.json"));
var rutaLista = Path.Combine(raiz, Arg("lista", cfg.Lista));
var rutaMando = Path.Combine(raiz, "datos", Arg("mando", "mando.json"));
void Log(string m) => Comun.Log(raiz, nombre, m);
string Capa() => $"{cfg.Canal}-{capa}";
string F(double seg, double fps) => ((long)Math.Round(seg * fps)).ToString(CultureInfo.InvariantCulture);

var grafica = siempre ? null : new Grafica(raiz, cfg, Log); // la emergencia no maneja graficas: las pone el Programador de la parrilla
var lista = new Lista();
var ef = Array.Empty<Entrada>();      // lista efectiva: la de la pantalla mas las tandas intercaladas
var inyec = new List<Inyeccion>();
var evT = DateTime.MinValue;
var volT = DateTime.MinValue;
var hayFg = false;                   // hay algo al aire: asi un cambio de video puede hacer fundido
var volUlt = DateTime.MinValue;
var rutaAudio = Path.Combine(raiz, "datos", "audio.json");
var listaT = DateTime.MinValue;
// la orden que ya estaba en el archivo al abrir es vieja: no se repite
var mandoT = File.Exists(rutaMando) ? File.GetLastWriteTimeUtc(rutaMando) : DateTime.MinValue;
var recargar = false;                 // la lista cambio: hay que volver a adelantar el siguiente
var guardado = DateTime.MinValue;
var e = Comun.Leer<Estado>(rutaEstado) ?? new Estado();
if (siempre) { e.Activo = true; e.Pausa = false; e.Fin = false; }
Log($"Inicio. {(e.Activo ? "Activo" : "Detenido")}. Ultimo estado: #{e.Idx + 1} {Path.GetFileName(e.Clip)} en {e.Pos:0.0}s.");

while (true)
{
    try
    {
        using var amcp = new Amcp(cfg.AmcpHost, cfg.AmcpPort);
        Log("Conectado a CasparCG.");
        while (true) { Paso(amcp); Thread.Sleep(250); }
    }
    catch (Exception ex)
    {
        Log("Sin conexion con CasparCG (" + ex.Message + "). Reintento en 2 s.");
        Thread.Sleep(2000);
    }
}

void Paso(Amcp amcp)
{
    var i = amcp.Info(cfg.Canal, capa);
    hayFg = i.Fg != null;
    var cambio = CargarLista();
    if (!siempre) Volumen(amcp);
    cambio |= Intercaladas();
    var orden = Mandos(amcp, i);
    if (!siempre && !orden && e.Activo && !e.Fin) orden = Eventos(amcp, i);
    cambio |= orden;
    var l = ef;
    // tras una orden, 'i' ya no refleja el motor: se decide en la vuelta siguiente
    if (!orden && e.Activo && !e.Pausa && !e.Fin && l.Length > 0)
    {
        var (idx, sig, accion) = Logica.Decidir(i, e.Idx, e.Sig, l.Select(x => x.Ruta).ToArray(), lista.Repetir);
        if (idx != e.Idx) { e.Idx = idx; e.Pos = 0; cambio = true; Log($"Al aire: #{idx + 1} {Path.GetFileName(l[idx].Ruta)}"); }
        e.Sig = sig;
        if (accion == Accion.Nada && recargar) accion = Accion.Cargar;

        if (accion == Accion.Reproducir)
        {
            // Solo se retoma la posicion guardada si el motor esta vacio (se reinicio); si no, desde el inicio del tramo.
            Reproducir(amcp, i.Fps, i.Fg == null ? e.Pos : 0);
            cambio = true;
        }
        else if (accion == Accion.Cargar)
        {
            recargar = false; e.Sig = -1;
            for (var n = 1; n <= l.Length; n++)
            {
                var c = (e.Idx + n) % l.Length;
                if (!lista.Repetir && c <= e.Idx) break; // sin repetir no se da la vuelta
                if (amcp.Ok($"LOADBG {Capa()} {Tramo(l[c], l[c].Ini, i.Fps, grafica?.FramesFundido(i.Fps) ?? 0)} AUTO")) { e.Sig = c; break; }
                Log($"FALTA #{c + 1} {l[c].Ruta}: no se pudo preparar, se salta.");
            }
            if (e.Sig < 0) amcp.Enviar($"LOADBG {Capa()} EMPTY"); // nada que adelantar: se quita lo que hubiera quedado de la lista anterior
            if (e.Sig < 0 && lista.Repetir) Log("ERROR: ningun elemento de la lista se puede reproducir.");
        }
        else if (accion == Accion.Fin) { e.Fin = true; cambio = true; Log("Fin de la lista (no se repite)."); }
        else { e.Pos = i.Pos; e.Dur = i.Dur; }
    }
    try { grafica?.Paso(amcp, i, e.Idx < l.Length ? l[e.Idx] : null, e.Activo); }
    catch (Exception ex) when (ex is not (IOException or System.Net.Sockets.SocketException)) { Log("Error en las graficas: " + ex.Message); } // nunca debe afectar al video
    // la pantalla sigue al elemento por su Id: si cambio (tambien por una orden), se guarda ya
    if (e.Idx < l.Length && e.Id != l[e.Idx].Id) { e.Id = l[e.Idx].Id; e.Clip = l[e.Idx].Ruta; cambio = true; }
    // Se guarda al cambiar algo y cada 2 s: es la senal de vida para el supervisor y el punto para retomar.
    if (cambio || (DateTime.Now - guardado).TotalSeconds >= 2) { e.Ts = guardado = DateTime.Now; Comun.Guardar(rutaEstado, e); }
}

// "ruta" SEEK cuadros [LENGTH cuadros]: el tramo de la entrada, empezando en 'desde'.
string Tramo(Entrada x, double desde, double fps, int mix = 0)
{
    desde = Math.Max(desde, x.Ini);
    var s = "\"" + x.Ruta.Replace('\\', '/') + "\"";
    if (mix > 0) s += " MIX " + mix; // fundido desde lo que esta al aire
    if (desde > 0.02) s += " SEEK " + F(desde, fps);
    if (x.Fin > desde + 0.2) s += " LENGTH " + F(x.Fin - desde, fps);
    return s;
}

void Reproducir(Amcp amcp, double fps, double desde)
{
    var l = ef;
    for (var n = 0; n < l.Length; n++, e.Idx = (e.Idx + 1) % l.Length, desde = 0)
    {
        if (amcp.Ok($"PLAY {Capa()} {Tramo(l[e.Idx], desde, fps, hayFg ? grafica?.FramesFundido(fps) ?? 0 : 0)}"))
        {
            Log($"Reproduciendo #{e.Idx + 1} {Path.GetFileName(l[e.Idx].Ruta)} desde {Math.Max(desde, l[e.Idx].Ini):0.0}s.");
            e.Sig = -1; e.Pos = desde; return;
        }
        Log($"FALTA #{e.Idx + 1} {l[e.Idx].Ruta}: no se pudo reproducir, se salta.");
    }
    Log("ERROR: ningun elemento de la lista se puede reproducir.");
}

// Vuelve a leer la lista si la pantalla la cambio, sin cortar lo que esta al aire.
bool CargarLista()
{
    var t = File.Exists(rutaLista) ? File.GetLastWriteTimeUtc(rutaLista) : DateTime.MinValue;
    if (t == listaT) return false;
    listaT = t;
    var nueva = Comun.Leer<Lista>(rutaLista) ?? new Lista();
    var n = nueva.Entradas.Length;
    lista = nueva; recargar = true;
    // shortcut: si el elemento al aire ya no esta en la lista, se salta al que quedo en su lugar; la pantalla no deja borrarlo.
    Reconstruir();
    Log(n == 0 ? "LA LISTA ESTA VACIA: no hay nada que emitir." : $"Lista cargada: {n} elementos.");
    return true;
}

// Volumen de la salida: lo deja la pantalla en audio.json; se aplica al cambiar y cada 3 s (un reinicio del motor lo devolveria a 100).
void Volumen(Amcp amcp)
{
    var t = File.Exists(rutaAudio) ? File.GetLastWriteTimeUtc(rutaAudio) : DateTime.MinValue;
    if (t == volT && (DateTime.Now - volUlt).TotalSeconds < 3) return;
    volT = t; volUlt = DateTime.Now;
    var v = Math.Clamp(Comun.Leer<AudioCfg>(rutaAudio)?.Vol ?? 1, 0, 1);
    amcp.Enviar($"MIXER {cfg.Canal} MASTERVOLUME {v.ToString("0.###", CultureInfo.InvariantCulture)}");
}

// La lista efectiva es la de la pantalla con las tandas intercaladas; el elemento al aire se sigue por su Id.
void Reconstruir()
{
    ef = Logica.Aplicar(lista.Entradas, inyec);
    var k = e.Id == "" ? -1 : Array.FindIndex(ef, x => x.Id == e.Id);
    e.Idx = ef.Length == 0 ? 0 : k >= 0 ? k : Math.Min(e.Idx, ef.Length - 1);
}

// true si lo que esta al aire es un spot o un video de una tanda (el programa retomado despues de una tanda no cuenta)
bool EnTanda() => e.Idx < ef.Length && (ef[e.Idx].Spot || inyec.Any(x => x.Entradas.Any(y => y.Id == e.Id && !y.Id.StartsWith("r~"))));

// Una tanda intercalada se quita cuando ya paso: al entrar el elemento que sigue. Tambien deja en el estado cual espera y cual esta al aire.
bool Intercaladas()
{
    var r = false;
    foreach (var inj in inyec.ToArray())
    {
        if (inj.Entradas.Any(x => x.Id == e.Id)) inj.Visto = true;
        else if (inj.Visto) { inyec.Remove(inj); Reconstruir(); e.Sig = -1; recargar = true; r = true; Log($"Termino la tanda \"{inj.Nombre}\"."); }
    }
    var pend = inyec.FirstOrDefault(x => !x.Visto)?.Nombre ?? "";
    string tanda = "", ancla = ""; int ti = 0, td = 0;
    var act = inyec.FirstOrDefault(x => x.Visto);
    if (act != null)
    {
        var sp = act.Entradas.Where(x => !x.Id.StartsWith("r~")).ToArray();
        ti = Array.FindIndex(sp, x => x.Id == e.Id) + 1;
        if (ti > 0) { tanda = act.Nombre; td = sp.Length; ancla = act.AnclaId; } else ti = 0;
    }
    if (pend != e.Pend || tanda != e.Tanda || ti != e.TandaIdx || td != e.TandaDe || ancla != e.TandaAncla) { e.Pend = pend; e.Tanda = tanda; e.TandaIdx = ti; e.TandaDe = td; e.TandaAncla = ancla; r = true; }
    return r;
}

// Intercala una tanda: "ahora" interrumpe el programa y lo retoma donde iba; "terminar" entra al acabar el programa (con sus cortes).
bool Tanda(Amcp amcp, Info i, string nombre, string[] clips, string modo, long n)
{
    var l = ef;
    if (clips.Length == 0 || e.Idx >= l.Length) { Log($"Tanda \"{nombre}\" ignorada: no hay videos o la lista esta vacia."); return false; }
    if (!e.Activo || e.Fin) { Log($"Tanda \"{nombre}\" ignorada: la emision esta detenida."); return false; }
    if (grafica?.Emerg == true) { Log($"Tanda \"{nombre}\" ignorada: esta la emergencia."); return false; }
    var cur = l[e.Idx];
    if (EnTanda()) { Log($"Tanda \"{nombre}\" ignorada: ya hay una tanda al aire."); return false; }
    var pend = inyec.FirstOrDefault(x => !x.Visto);
    if (modo != "ahora" && pend != null) { Log($"Tanda \"{nombre}\" ignorada: ya hay una en espera."); return false; }
    var spots = clips.Select((c, j) => new Entrada { Id = $"i{n}:{j}", Ruta = c, Spot = true }).ToArray();
    if (modo == "ahora")
    {
        if (pend != null) { inyec.Remove(pend); Reconstruir(); } // segundo clic: la que esperaba sale ya
        var pos = Math.Max(cur.Ini, i.Pos);
        var fin = cur.Fin > 0 ? cur.Fin : i.Final;
        var ent = spots.ToList();
        if (fin <= 0 || fin - pos > 1)
            ent.Add(new Entrada { Id = $"r~{cur.Id}~{n}", Ruta = cur.Ruta, Ini = pos, Fin = cur.Fin, Spot = cur.Spot, Titulo = cur.Titulo, Proximo = cur.Proximo, Zoc = cur.Zoc, Grupo = cur.Grupo, Dur = cur.Dur });
        inyec.Add(new Inyeccion { AnclaId = cur.Id, Entradas = [.. ent], Nombre = nombre });
        Reconstruir();
        e.Idx = Array.FindIndex(ef, x => x.Id == spots[0].Id);
        e.Pausa = false;
        Log($"TANDA \"{nombre}\" AHORA ({spots.Length} videos); el programa se retoma en {Math.Max(cur.Ini, i.Pos):0} s.");
        Reproducir(amcp, i.Fps, 0);
        return true;
    }
    var a = e.Idx;
    while (cur.Grupo != "" && a + 1 < l.Length && l[a + 1].Grupo == cur.Grupo) a++;
    inyec.Add(new Inyeccion { AnclaId = l[a].Id, Entradas = spots, Nombre = nombre });
    Reconstruir(); e.Sig = -1; recargar = true;
    Log($"TANDA \"{nombre}\" en espera: sale al terminar \"{Path.GetFileName(l[a].Ruta)}\".");
    return true;
}

// Tandas a hora fija: salen aunque la ventana este cerrada. Se lanzan dentro de los 90 s siguientes a la hora, una vez al dia.
bool Eventos(Amcp amcp, Info i)
{
    if ((DateTime.Now - evT).TotalSeconds < 1) return false;
    evT = DateTime.Now;
    var hoy = evT.ToString("yyyy-MM-dd"); var s = evT.TimeOfDay.TotalSeconds; var r = false;
    foreach (var ev in lista.Eventos)
    {
        var k = ev.Id + "|" + ev.Hora; var dif = s - Logica.HoraSeg(ev.Hora);
        if (dif < 0 || dif >= 90 || e.Hechos.GetValueOrDefault(k) == hoy) continue;
        e.Hechos[k] = hoy; r = true;
        var modo = ev.Modo == "interrumpir" && !EnTanda() ? "ahora" : "terminar"; // en medio de otra tanda espera a que acabe
        Log($"HORA FIJA {ev.Hora}: tanda \"{ev.Nombre}\" ({(modo == "ahora" ? "interrumpe y retoma" : "al terminar el programa")}).");
        r |= Tanda(amcp, i, ev.Nombre, ev.Clips, modo, DateTime.UtcNow.Ticks);
        if (modo == "ahora" && e.Idx < ef.Length) return true; // el motor cambio: se decide en la vuelta siguiente
    }
    return r;
}

// shortcut: una orden por archivo; dos ordenes en menos de 250 ms y la primera se pierde. Pasar a una cola si hace falta.
bool Mandos(Amcp amcp, Info i)
{
    var t = File.Exists(rutaMando) ? File.GetLastWriteTimeUtc(rutaMando) : DateTime.MinValue;
    if (t == mandoT) return false;
    mandoT = t;
    var m = Comun.Leer<Mando>(rutaMando);
    if (m == null || m.N == e.MandoN) return false;
    e.MandoN = m.N;
    if (siempre && m.Cmd is "aire" or "parar" or "tanda") return false;
    var n = ef.Length;
    switch (m.Cmd)
    {
        case "tanda": return Tanda(amcp, i, m.Nombre, m.Clips, m.Modo, m.N) || true;
        case "aire" or "ir":
            if (n == 0) { Log("Orden " + m.Cmd + " ignorada: la lista esta vacia."); break; }
            e.Activo = true; e.Pausa = false; e.Fin = false;
            e.Idx = Math.Clamp((int)m.Arg, 0, n - 1);
            Log(m.Cmd == "aire" ? "AL AIRE (orden de la pantalla)." : $"Salto manual a #{e.Idx + 1}.");
            Reproducir(amcp, i.Fps, 0);
            break;
        case "parar":
            e.Activo = false; e.Pausa = false; e.Fin = false; e.Sig = -1;
            amcp.Enviar($"CLEAR {Capa()}");
            Log("EMISION DETENIDA (orden de la pantalla).");
            break;
        case "buscar": amcp.Enviar($"CALL {Capa()} SEEK {F(m.Arg, i.Fps)}"); break;
        case "pausa": amcp.Enviar($"PAUSE {Capa()}"); e.Pausa = true; Log("Pausa manual."); break;
        case "reanudar": amcp.Enviar($"RESUME {Capa()}"); e.Pausa = false; Log("Se reanuda la emision."); break;
        default: Log("Orden desconocida: " + m.Cmd); break;
    }
    return true;
}

public enum Accion { Nada, Cargar, Reproducir, Fin }

// Una tanda intercalada en la lista, justo despues del elemento 'AnclaId'.
public class Inyeccion
{
    public string AnclaId = "", Nombre = "";
    public Entrada[] Entradas = [];
    public bool Visto; // ya entro al aire alguno de sus elementos
}

public static class Logica
{
    static bool Igual(string? a, string b) => string.Equals(a?.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    // Decide que hacer mirando lo que CasparCG tiene al aire (fg) y adelantado (bg).
    public static (int idx, int sig, Accion a) Decidir(Info i, int idx, int sig, string[] l, bool repetir = true)
    {
        if (i.Fg == null) return (idx, -1, Accion.Reproducir);                    // motor vacio (recien abierto o reiniciado)
        if (i.Bg != null) return Igual(i.Fg, l[idx]) ? (idx, sig, Accion.Nada) : (idx, -1, Accion.Reproducir);
        var habia = sig >= 0;
        if (habia) idx = sig;                                                     // el adelantado ya entro al aire
        if (!Igual(i.Fg, l[idx])) return (idx, -1, Accion.Reproducir);            // al aire hay algo ajeno a la lista
        var ultimo = !repetir && idx == l.Length - 1;
        if (!habia && i.Final > 0 && i.Pos >= i.Final - 0.2)                      // termino sin tener siguiente
            return ultimo ? (idx, -1, Accion.Fin) : ((idx + 1) % l.Length, -1, Accion.Reproducir);
        return (idx, -1, ultimo ? Accion.Nada : Accion.Cargar);
    }

    // La lista de la pantalla con las tandas intercaladas despues de su elemento ancla; si el ancla ya no existe, la tanda se descarta.
    public static Entrada[] Aplicar(Entrada[] baseL, List<Inyeccion> inyec)
    {
        var o = baseL.ToList();
        foreach (var inj in inyec.ToArray())
        {
            var p = o.FindIndex(x => x.Id == inj.AnclaId);
            if (p < 0) inyec.Remove(inj); else o.InsertRange(p + 1, inj.Entradas);
        }
        return [.. o];
    }

    public static double HoraSeg(string h) // "21:00" o "21:00:30" -> segundos desde la medianoche
    {
        var p = (h ?? "").Split(':');
        return (p.Length > 0 && double.TryParse(p[0], out var a) ? a * 3600 : 0) + (p.Length > 1 && double.TryParse(p[1], out var b) ? b * 60 : 0) + (p.Length > 2 && double.TryParse(p[2], out var c) ? c : 0);
    }

    public static void Probar()
    {
        Entrada E(string id) => new() { Id = id, Ruta = id };
        var inj = new List<Inyeccion> { new() { AnclaId = "B", Entradas = [E("s1"), E("s2"), E("r")] }, new() { AnclaId = "ZZ", Entradas = [E("x")] } };
        var ids = string.Join(",", Aplicar([E("A"), E("B"), E("C")], inj).Select(x => x.Id));
        if (ids != "A,B,s1,s2,r,C" || inj.Count != 1) throw new Exception("Aplicar: " + ids);
        if (HoraSeg("21:30") != 77400 || HoraSeg("00:00:30") != 30) throw new Exception("HoraSeg");
        string[] l = ["A", "B", "A"];
        void Es(Info i, int idx, int sig, (int, int, Accion) esperado, bool rep = true)
        { var r = Decidir(i, idx, sig, l, rep); if (r != esperado) throw new Exception($"Esperaba {esperado}, salio {r}"); }
        Es(new Info(null, null, 0, 0, 30), 1, 2, (1, -1, Accion.Reproducir));   // motor vacio: reproducir el guardado
        Es(new Info("A", null, 5, 60, 30), 0, -1, (0, -1, Accion.Cargar));      // al aire sin siguiente: adelantar
        Es(new Info("A", "B", 5, 60, 30), 0, 1, (0, 1, Accion.Nada));           // todo en orden
        Es(new Info("B", null, 1, 60, 30), 0, 1, (1, -1, Accion.Cargar));       // el siguiente entro: avanzar y adelantar otro
        Es(new Info("a", null, 1, 60, 30), 1, 2, (2, -1, Accion.Cargar));       // mismo nombre repetido y sin distinguir mayusculas
        Es(new Info("A", null, 60, 60, 30), 0, -1, (1, -1, Accion.Reproducir)); // termino sin siguiente
        Es(new Info("X", null, 5, 60, 30), 0, -1, (0, -1, Accion.Reproducir));  // al aire hay algo ajeno a la lista
        Es(new Info("A", null, 30, 60, 30, 10, 20), 0, -1, (1, -1, Accion.Reproducir)); // termino un tramo con fin marcado (10+20)
        Es(new Info("A", null, 5, 60, 30), 2, -1, (2, -1, Accion.Nada), false);  // ultimo y sin repetir: no adelantar
        Es(new Info("A", null, 60, 60, 30), 2, -1, (2, -1, Accion.Fin), false);  // ultimo termino y sin repetir: fin
        var x = Amcp.Interpretar("<channel><framerate>30000</framerate><framerate>1001</framerate><stage><layer><layer_10><background><file><name>B</name></file></background><foreground><file><clip>10</clip><clip>5</clip><name>A</name><time>1.5</time><time>60</time></file></foreground></layer_10></layer></stage></channel>", 10);
        if (x.Fg != "A" || x.Bg != "B" || x.Pos != 1.5 || x.Dur != 60 || x.Final != 15 || Math.Abs(x.Fps - 29.97) > 0.01) throw new Exception("Interpretar: " + x);
        if (Amcp.Interpretar("<channel><framerate>25</framerate><framerate>1</framerate></channel>", 10).Fg != null) throw new Exception("Interpretar vacio");
    }
}
