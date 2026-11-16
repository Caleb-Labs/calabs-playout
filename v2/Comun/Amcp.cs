// Cliente minimo del protocolo AMCP de CasparCG (texto por TCP).
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace CaLabs;

public record Info(string? Fg, string? Bg, double Pos, double Dur, double Fps, double Ini = 0, double Largo = 0)
{
    public double Final => Largo > 0 ? Ini + Largo : Dur; // segundo del archivo donde termina este tramo
}

public sealed class Amcp : IDisposable
{
    readonly TcpClient c = new();
    readonly StreamReader r;
    readonly StreamWriter w;

    public Amcp(string host, int puerto)
    {
        c.Connect(host, puerto);
        var s = c.GetStream();
        s.ReadTimeout = s.WriteTimeout = 5000; // si CasparCG se cuelga, salta una excepcion en vez de esperar para siempre
        r = new StreamReader(s, Encoding.UTF8);
        w = new StreamWriter(s, new UTF8Encoding(false)) { NewLine = "\r\n" };
    }

    // Devuelve la linea de estado ("202 PLAY OK", "404 PLAY FAILED"...).
    public string Enviar(string cmd)
    {
        var l = Pedir(cmd);
        if (l.StartsWith("201")) Leer(); // 201 = sigue una linea de datos (CALL, VERSION): se descarta para no desordenar las respuestas
        return l;
    }

    string Leer() => r.ReadLine() ?? throw new IOException("CasparCG cerro la conexion");

    string Pedir(string cmd)
    {
        w.WriteLine(cmd); w.Flush();
        string l;
        do { l = Leer(); } while (l.Length == 0);
        return l;
    }

    public bool Ok(string cmd) => Enviar(cmd).StartsWith('2');

    string InfoXml(int canal, int capa)
    {
        var est = Pedir($"INFO {canal}-{capa}");
        if (!est.StartsWith("201")) throw new IOException("INFO fallo: " + est);
        var sb = new StringBuilder();
        while (true)
        {
            var l = Leer();
            sb.AppendLine(l);
            if (l.Trim() == "</channel>") break;
        }
        return sb.ToString();
    }

    public Info Info(int canal, int capa) => Interpretar(InfoXml(canal, capa), capa);

    // true si la capa no tiene ningun productor (CALL sobre una capa vacia no da error, por eso se mira aparte)
    public bool CapaVacia(int canal, int capa) =>
        XDocument.Parse(InfoXml(canal, capa)).Descendants("layer_" + capa).FirstOrDefault()?.Element("foreground")?.Element("producer")?.Value is null or "empty";

    public static Info Interpretar(string xml, int capa)
    {
        var doc = XDocument.Parse(xml);
        static double N(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var fr = doc.Root!.Elements("framerate").Select(e => N(e.Value)).ToArray();
        var l = doc.Descendants("layer_" + capa).FirstOrDefault();
        var fg = l?.Element("foreground")?.Element("file");
        var t = fg?.Elements("time").Select(e => N(e.Value)).ToArray() ?? [];
        var c = fg?.Elements("clip").Select(e => N(e.Value)).ToArray() ?? [];
        return new Info(
            fg?.Element("name")?.Value,
            l?.Element("background")?.Element("file")?.Element("name")?.Value,
            t.Length > 0 ? t[0] : 0, t.Length > 1 ? t[1] : 0,
            fr.Length > 1 && fr[1] > 0 ? fr[0] / fr[1] : 25,
            c.Length > 0 ? c[0] : 0, c.Length > 1 ? c[1] : 0);
    }

    public void Dispose() => c.Dispose();
}
