// CaLabs Playout 2.0 - Graficas al aire: logo, reloj, zocalo y ticker. CasparCG dibuja una pagina HTML (ui\grafica) en la capa 30, encima
// del video y de la emergencia, y esa imagen sale igual por la pantalla completa y por NDI.
// Lo decide el Programador, no la ventana: con la Parrilla cerrada el logo sigue. Si la pagina se cae, el video sigue sin graficos y aqui se vuelve a abrir.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CaLabs;

public sealed class Grafica(string raiz, Config cfg, Action<string> log)
{
    readonly string rutaG = Path.Combine(raiz, "datos", "grafica.json"), rutaEm = Path.Combine(raiz, "datos", "emergencia-estado.json");
    readonly string url = new Uri(Path.Combine(raiz, "ui", "grafica", "index.html")).AbsoluteUri;
    DateTime gT = DateTime.MinValue, emT = DateTime.MinValue, ultEnvio = DateTime.MinValue, noAntes = DateTime.MinValue, zocProx = DateTime.MaxValue;
    JsonNode? g;
    string logo = "", ultJson = "", zocId = "";
    bool emerg, abierta = true;
    public bool Emerg => emerg;
    double ultPos;

    // Regla: en la emergencia solo el logo; en un spot o un programa de menos de 'minDur' segundos, nada; el resto segun los ajustes.
    public static (bool logo, bool hora, bool ctx) Visible(bool mosca, bool hora, bool emerg, bool corto) =>
        emerg ? (mosca, false, false) : corto ? (false, false, false) : (mosca, hora, true);

    // Fundido entre videos (cuadros de la transicion): 0 = corte directo. Lo elige el operador en la pestaña Graficas.
    public int FramesFundido(double fps) => g?["trans"]?.ToString() == "fundido" ? (int)Math.Round((Num("fade") is var f && f > 0 ? Math.Clamp(f, 0.2, 5) : 0.5) * fps) : 0;

    bool Si(string k) => g?[k]?.GetValue<bool>() ?? false;
    double Num(string k) => g?[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;

    void Cargar()
    {
        var t = File.Exists(rutaG) ? File.GetLastWriteTimeUtc(rutaG) : DateTime.MinValue;
        if (t != gT)
        {
            gT = t;
            try
            {
                var o = JsonNode.Parse(File.ReadAllText(rutaG))!;
                g = o["g"]; var l = (string?)o["logo"] ?? "";
                var f = l.StartsWith("datos/") ? Path.Combine(raiz, l) : Path.Combine(raiz, "ui", "playout", l);
                logo = new Uri(f).AbsoluteUri + (File.Exists(f) ? "?v=" + File.GetLastWriteTimeUtc(f).Ticks : "");
            }
            catch { /* a medio escribir: queda la configuracion anterior */ }
        }
        if ((DateTime.Now - emT).TotalSeconds >= 1)
        {
            emT = DateTime.Now;
            emerg = Comun.Leer<Emergencia>(rutaEm) is { Activa: true };
        }
    }

    bool Llamar(Amcp a, string fn, string json) =>
        a.Ok($"CALL {cfg.Canal}-{cfg.CapaGrafica} \"{fn}('{Convert.ToBase64String(Encoding.UTF8.GetBytes(json))}')\"");

    // x = elemento de la parrilla al aire (null si nada); i = lo que tiene CasparCG en la capa de la parrilla.
    public void Paso(Amcp a, Info i, Entrada? x, bool activo)
    {
        Cargar();
        if (g == null) return; // la pantalla todavia no dejo su configuracion
        var enAire = x != null && activo && i.Fg != null && string.Equals(i.Fg.Replace('\\', '/'), x.Ruta.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        var largo = i.Largo > 0 ? i.Largo : Math.Max(0, i.Dur - i.Ini);
        var dur = x?.Dur > 0 ? x.Dur : largo; // un programa cortado en tramos cuenta completo
        var corto = enAire && (x!.Spot || (dur > 0 && dur < Num("minDur")));
        var v = Visible(Si("mosca"), Si("hora"), emerg, corto);
        var json = new JsonObject { ["g"] = g.DeepClone(), ["logo"] = logo, ["vis"] = new JsonObject { ["logo"] = v.logo, ["hora"] = v.hora && Si("hora"), ["zoc"] = v.ctx, ["tk"] = v.ctx } }.ToJsonString();

        // zocalo automatico: 2,5 s despues de que empieza un programa (tambien si se vuelve a lanzar el mismo), y luego cada 'zocRep' minutos
        if (i.Pos < ultPos - 1) zocId = "";
        ultPos = i.Pos;
        if (enAire && v.ctx && !x!.Spot && x.Zoc && Si("zocalo"))
        {
            if (x.Id != zocId) { zocId = x.Id; zocProx = DateTime.Now.AddSeconds(2.5); }
            if (DateTime.Now >= zocProx && DateTime.Now > noAntes)
            {
                Llamar(a, "zoc", JsonSerializer.Serialize(new { a = x.Titulo, b = x.Proximo }));
                zocProx = Num("zocRep") > 0 ? DateTime.Now.AddMinutes(Num("zocRep")) : DateTime.MaxValue;
            }
        }
        else zocId = "";

        if (DateTime.Now < noAntes || (json == ultJson && (DateTime.Now - ultEnvio).TotalSeconds < 5)) return;
        ultJson = json; ultEnvio = DateTime.Now;
        if (!a.CapaVacia(cfg.Canal, cfg.CapaGrafica) && Llamar(a, "estado", json)) { abierta = true; return; }
        // la capa esta vacia: la pagina no esta (CasparCG recien abierto o reiniciado). Se abre y se le da tiempo para cargar.
        if (abierta) log("Capa de graficas sin pagina: se abre.");
        abierta = false;
        a.Enviar($"PLAY {cfg.Canal}-{cfg.CapaGrafica} [HTML] \"{url}\"");
        noAntes = DateTime.Now.AddSeconds(4); ultJson = "";
    }

    public static void Probar()
    {
        void Es((bool, bool, bool) r, (bool, bool, bool) e) { if (r != e) throw new Exception($"Graficas: esperaba {e}, salio {r}"); }
        Es(Visible(true, true, false, false), (true, true, true));    // programa normal: todo segun ajustes
        Es(Visible(true, true, false, true), (false, false, false));  // spot o programa corto: nada
        Es(Visible(true, true, true, false), (true, false, false));   // emergencia: solo el logo
        Es(Visible(true, true, true, true), (true, false, false));    // emergencia aunque el video sea corto: solo el logo
        Es(Visible(false, true, true, false), (false, false, false)); // logo apagado en los ajustes
    }
}
