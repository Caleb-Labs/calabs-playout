// CaLabs Playout 2.0 - codigo compartido por el Supervisor y el Programador.
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CaLabs;

public class Config
{
    public string CasparDir { get; set; } = "";
    public string AmcpHost { get; set; } = "127.0.0.1";
    public int AmcpPort { get; set; } = 5250;
    public int Canal { get; set; } = 1;
    public int Capa { get; set; } = 10;                 // capa de la parrilla
    public string Lista { get; set; } = "lista.json";
    public int CapaEmergencia { get; set; } = 20;       // encima de la parrilla: la tapa
    public string ListaEmergencia { get; set; } = "emergencia.json";
    public int PuertoControl { get; set; } = 5260;      // ordenes al supervisor (solo desde este PC)
    public int AtascoSeg { get; set; } = 10;            // segundos sin avanzar antes de entrar en emergencia
    public int CapaGrafica { get; set; } = 30;          // logo, reloj, zocalo y ticker: encima de todo
    public int PuertoPrevia { get; set; } = 9260;
    public int PuertoOsc { get; set; } = 6251;          // por aqui CasparCG avisa del nivel de audio (vumetro)       // por aqui CasparCG manda la vista previa a la ventana
}

// Volumen de la salida (0 a 1): lo deja la pantalla y el Programador lo aplica al motor, tambien despues de un reinicio.
public class AudioCfg
{
    public double Vol { get; set; } = 1;
}

public class Emergencia
{
    public bool Activa { get; set; }
    public string Motivo { get; set; } = "";
    public DateTime Desde { get; set; }
    public bool VolverPend { get; set; }   // se pidio volver cuando termine el video de emergencia
    public int VolverIdx { get; set; }     // elemento de la parrilla desde el que se vuelve
}

// Un tramo de video a emitir. Ini y Fin en segundos del archivo (Fin 0 = hasta el final).
public class Entrada
{
    public string Id { get; set; } = "";    // lo pone la pantalla; sirve para seguir al mismo elemento si la lista cambia
    public string Ruta { get; set; } = "";
    public double Ini { get; set; }
    public double Fin { get; set; }
    public bool Spot { get; set; }          // viene de un bloque de spots: sin logo, reloj, zocalo ni ticker
    public string Titulo { get; set; } = "";   // para el zocalo
    public string Proximo { get; set; } = "";  // titulo del programa que sigue ("" = ninguno)
    public bool Zoc { get; set; } = true;      // este programa admite zocalo
    public string Grupo { get; set; } = "";    // elemento de la parrilla del que sale (un programa con sus cortes): "esperar que termine" espera al ultimo del grupo
    public double Dur { get; set; }            // duracion del programa completo, para la regla de "programa corto" (0 = la del tramo)
}

// Tanda a hora fija: la lleva el Programador aunque la ventana este cerrada.
public class Evento
{
    public string Id { get; set; } = "";
    public string Hora { get; set; } = "";     // HH:mm o HH:mm:ss
    public string Modo { get; set; } = "interrumpir";  // interrumpir = entra ahora y se retoma · esperar = al terminar el programa
    public string Nombre { get; set; } = "";
    public string[] Clips { get; set; } = [];
}

public class Lista
{
    public bool Repetir { get; set; } = true;
    public Entrada[] Entradas { get; set; } = [];
    public Evento[] Eventos { get; set; } = [];
}

// Orden de la pantalla al Programador: aire, ir, parar, buscar, pausa, reanudar.
public class Mando
{
    public long N { get; set; }             // numero de orden: solo se ejecuta si cambia
    public string Cmd { get; set; } = "";
    public double Arg { get; set; }
    public string Nombre { get; set; } = "";   // tanda: nombre, spots y cuando entra (ahora | terminar)
    public string Modo { get; set; } = "";
    public string[] Clips { get; set; } = [];
}

public class Estado
{
    public bool Activo { get; set; }        // false = detenido (no se emite la parrilla)
    public bool Pausa { get; set; }
    public bool Fin { get; set; }           // la lista termino y no se repite
    public int Idx { get; set; }            // elemento al aire
    public int Sig { get; set; } = -1;      // elemento ya adelantado a CasparCG (-1 = ninguno)
    public string Id { get; set; } = "";
    public string Clip { get; set; } = "";
    public double Pos { get; set; }
    public double Dur { get; set; }
    public long MandoN { get; set; }        // ultima orden ejecutada
    public string Pend { get; set; } = "";  // tanda esperando a que termine el programa
    public string Tanda { get; set; } = ""; // tanda intercalada que esta al aire
    public int TandaIdx { get; set; }
    public int TandaDe { get; set; }
    public string TandaAncla { get; set; } = ""; // elemento de la parrilla al que se le intercalo la tanda (para mostrar en que fila va)
    public Dictionary<string, string> Hechos { get; set; } = []; // tandas a hora fija ya lanzadas hoy ("id|hora" -> fecha)
    public DateTime Ts { get; set; }
}

public static class Comun
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // La raiz es la carpeta que contiene config.json, buscando hacia arriba desde el .exe.
    public static string Raiz()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "config.json"))) return d.FullName;
        throw new FileNotFoundException("No se encontro config.json");
    }

    public static Config LeerConfig(string raiz)
    {
        var c = Leer<Config>(Path.Combine(raiz, "config.json")) ?? new Config();
        // instalado: el motor va en la carpeta "motor" junto al programa
        if (string.IsNullOrWhiteSpace(c.CasparDir) || !Directory.Exists(c.CasparDir)) c.CasparDir = Path.Combine(raiz, "motor");
        return c;
    }

    public static T? Leer<T>(string ruta)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(ruta), Json); } catch { return default; }
    }

    // Escribe a un temporal y lo renombra: un corte de luz no deja el archivo a medias.
    public static void Guardar<T>(string ruta, T dato)
    {
        var tmp = ruta + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(dato, Json));
        // otro programa puede estar leyendolo justo ahora (Windows no deja reemplazarlo): se reintenta un momento
        for (var n = 0; ; n++)
            try { File.Move(tmp, ruta, true); return; }
            catch (Exception ex) when (n < 10 && ex is IOException or UnauthorizedAccessException) { Thread.Sleep(20); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string dispositivo, int modo, IntPtr datos);

    // Tamano y posicion reales (en pixeles) de la pantalla n de Windows; ancho 0 si no esta conectada.
    public static (int an, int al, int x, int y) Pantalla(int n)
    {
        var p = Marshal.AllocHGlobal(220); // DEVMODEW: se leen dmPosition (76, 80), dmPelsWidth (172) y dmPelsHeight (176)
        try
        {
            for (var k = 0; k < 220; k += 4) Marshal.WriteInt32(p, k, 0);
            Marshal.WriteInt16(p, 68, 220); // dmSize
            return EnumDisplaySettingsW($@"\\.\DISPLAY{n}", -1, p) ? (Marshal.ReadInt32(p, 172), Marshal.ReadInt32(p, 176), Marshal.ReadInt32(p, 76), Marshal.ReadInt32(p, 80)) : (0, 0, 0, 0);
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    // Dispositivo de audio por donde sale el aire segun la configuracion del motor ("" = el predeterminado de Windows).
    public static string AudioAire(Config cfg)
    {
        try
        {
            var n = System.Xml.Linq.XDocument.Load(Path.Combine(cfg.CasparDir, "casparcg.config")).Descendants("system-audio").FirstOrDefault()?.Element("device-name")?.Value ?? "";
            return n.StartsWith("OpenAL Soft on ") ? n["OpenAL Soft on ".Length..] : n;
        }
        catch { return ""; }
    }

    public static string Texto<T>(T dato) => JsonSerializer.Serialize(dato, Json);

    public static void Log(string raiz, string quien, string msg)
    {
        var linea = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{quien}] {msg}";
        Console.WriteLine(linea);
        try
        {
            var ruta = Path.Combine(raiz, "datos", $"registro-{DateTime.Now:yyyy-MM-dd}.log");
            using var f = new FileStream(ruta, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var w = new StreamWriter(f);
            w.WriteLine(linea);
        }
        catch { } // el registro nunca debe tumbar al programa
    }
}
