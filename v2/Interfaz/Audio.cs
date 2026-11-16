// CaLabs Playout 2.0 - "Mi escucha": repite por otro dispositivo (audifonos) lo que suena en la salida del aire, con su propio volumen.
// Se toma con la captura "loopback" de Windows de ese dispositivo, asi que no toca lo que sale al aire.
// shortcut: si el dispositivo del aire es compartido con otros programas, tambien se oye lo que ellos reproduzcan.
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CaLabs;

static class Audio
{
    static readonly object c = new();
    static WasapiLoopbackCapture? cap;
    static WasapiOut? sal;
    static VolumeSampleProvider? vol;
    static long bytes;

    public static List<string> Salidas()
    {
        using var en = new MMDeviceEnumerator();
        return en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select(d => d.FriendlyName).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void Volumen(float v) { lock (c) if (vol != null) vol.Volume = Math.Clamp(v, 0, 1); }
    public static long Bytes { get { lock (c) return bytes; } }
    public static void Detener()
    {
        lock (c)
        {
            try { cap?.StopRecording(); } catch { }
            try { sal?.Stop(); } catch { }
            cap?.Dispose(); sal?.Dispose(); cap = null; sal = null; vol = null;
        }
    }

    // origen = salida del aire ("" = la predeterminada de Windows); destino = donde se escucha. Devuelve "" si salio bien o el motivo del fallo.
    public static string Escuchar(string origen, string destino, float volumen)
    {
        lock (c)
        {
            Detener();
            if (destino == "") return "Elige el dispositivo de «Mi escucha».";
            try
            {
                using var en = new MMDeviceEnumerator();
                var todos = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
                var dOrigen = origen == "" ? en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : todos.FirstOrDefault(d => d.FriendlyName == origen);
                var dDestino = todos.FirstOrDefault(d => d.FriendlyName == destino);
                if (dOrigen == null) return "No encontré el dispositivo por donde sale el aire.";
                if (dDestino == null) return "No encontré el dispositivo de «Mi escucha» (¿está conectado?).";
                if (dDestino.ID == dOrigen.ID) return "«Mi escucha» y el aire usan el mismo dispositivo: elige otro para escuchar.";
                bytes = 0;
                cap = new WasapiLoopbackCapture(dOrigen);
                var buf = new BufferedWaveProvider(cap.WaveFormat) { DiscardOnBufferOverflow = true, ReadFully = true, BufferDuration = TimeSpan.FromMilliseconds(500) };
                cap.DataAvailable += (_, e) => { buf.AddSamples(e.Buffer, 0, e.BytesRecorded); Interlocked.Add(ref bytes, e.BytesRecorded); };
                vol = new VolumeSampleProvider(buf.ToSampleProvider()) { Volume = Math.Clamp(volumen, 0, 1) };
                sal = new WasapiOut(dDestino, AudioClientShareMode.Shared, true, 120);
                sal.Init(vol); sal.Play(); cap.StartRecording();
                return "";
            }
            catch (Exception ex) { Detener(); return "No se pudo activar «Mi escucha»: " + ex.Message; }
        }
    }
}
