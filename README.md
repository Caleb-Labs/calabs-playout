<div align="center">

<img src="Logos/CaLabs_logo_completo_sin_fondo.png" alt="CaLabs" width="260">

# 📺 CaLabs Playout

**A modern TV playout system that runs in your browser.**
*Un sistema de playout para televisión que funciona en el navegador.*

🚧 `Browser prototype v3.4` · `Native version 2.0 (CasparCG + NDI) · in development` 🚧

</div>

---

## 🇬🇧 English

CaLabs Playout is a **TV playout prototype built as a single HTML file**. A modern alternative to old, expensive and unintuitive broadcast software, ideal for people getting started in TV scheduling and media ingest.

> ⚠️ **Language note:** for now the interface is **in Spanish only**. Multi-language support (English included) is **planned and being worked on**. 🌍

![On air](docs/01_on-air.png)

### ✨ What it does today
- 🗓️ Schedule grid with a running counter of loaded hours and a free target length
- ✂️ Ad breaks *inside* a program, using saved spot groups
- 📢 Break blocks and fixed-time events
- 🖥️ Separate output window (and extra outputs) with seamless switching between videos
- 🎨 On-screen graphics: logo bug with effects, clock, lower third and ticker
- 🔊 Level meter plus separate output and monitoring volume
- 🧩 Resizable panels and saved templates

![Output window](docs/02_output-window.png)

### 🚀 Try it
1. Download or clone this repository.
2. Double-click `Abrir CaLabs Playout.bat` (Windows, uses Chrome or Edge), or open `CaLabs_Playout.html` in Chrome.
3. Load your own MP4 videos from the Library tab.
4. Have MPG, AVI or WMV files? Use `Convertir MPG a MP4.bat` (needs FFmpeg).

### 🧱 Native version 2.0 — CasparCG + NDI (in development)

A rewrite as a **native Windows program** (C# / .NET 10) on top of the **CasparCG 2.5** engine, with **NDI output**. The browser prototype above stays as it is; the new source lives in [`v2/`](v2/).

**✅ What's new in 2.0**
- 🛡️ Built to stay on air: engine, scheduler and control window are **independent programs**. A supervisor restarts whatever freezes or dies (engine, scheduler, window), and the picture keeps playing if the window is closed.
- 🚨 Emergency playlist that takes over by itself if the video stops advancing; going back to the schedule is manual (*now* or *when the emergency clip ends*).
- ✂️ Ad breaks **inside** a program (the program resumes exactly where it left off), "Break now" (*now* / *when the program ends*) and fixed-time breaks, all run by the engine even with the window closed.
- 🎨 Graphics on air (logo bug with effects, clock, lower third, ticker) drawn by the engine, so they go out on the **screen output and NDI**. Rules built in: no graphics on ads or very short clips, logo only on emergency, ticker and lower third take turns.
- 🔀 Optional fade between videos and optional "skip black + silence at the end of a program".
- 🖥️ Full-screen output on any connected monitor, an "emergency exit" hotkey (`Ctrl+Alt+V`) and a tray icon.
- 🔊 Real level meter and volume from the engine, fixed audio output device (with a red warning if it is missing) and a private "listen" output.
- 💾 Everything you save is also written to a file with automatic backups, and restored if the program's memory is deleted.
- 📦 Windows installer with terms & conditions, install-folder choice and start-with-Windows option (see [`v2/Instalador`](v2/Instalador)).

**🔜 Still to come**
- [ ] 🔄 "Check for updates" button and a "what's new" window
- [ ] 🧰 One-click Windows settings script (no sleep, etc.)
- [ ] 📲 Telegram alerts to a group, with a "I'll take it" button
- [ ] 📹 Live inputs: capture cards, ATEM Mini, web streams and NDI input, with preview and a manual "Go live" (no automation during a live feed)
- [ ] 🕵️ Optional black / silence watchdog
- [ ] 📄 PDF export and longer soak tests

**🛠️ Build it** — you need the [.NET 10 SDK](https://dotnet.microsoft.com/) and the CasparCG 2.5.1 release (download it from its official page; it is **not** included here). NDI output needs [NDI Tools](https://ndi.video/). To make the installer you also need [Inno Setup 6](https://jrsoftware.org/isinfo.php): run `v2/Instalador/construir.ps1 -Motor "<path to CasparCG>"`.

### 🗺️ Coming in future updates

**🔜 Next update (v3.5)**
- [ ] 🎨 Color themes: Day / Night / Neutral (always built from the CaLabs palette) plus a Custom theme with free colors
- [ ] 🌍 Language switch (Spanish / English)
- [ ] 📊 Classic green-yellow-red level meter with dB scale
- [ ] 💾 Spot groups: see which videos each saved group contains and reload any of them
- [ ] 🔀 Smoother crossfade between videos (fix in progress)
- [ ] 🧱 Lower third: adjustable spacing between bars, independent or joint positioning, custom colors
- [ ] 🖱️ Double-click on panel dividers to reset their size
- [ ] 🎧 Audio output device selection

**🛰️ Later**
- [ ] 🔌 Final version on CasparCG with NDI output
- [ ] 📹 Live inputs (HDMI capture, NDI, ATEM, OBS)
- [ ] 🌐 External stream hook and a clean extra output
- [ ] 💿 Windows installer

> 💡 Ideas or feedback? Open an issue. 🙌

### 🤖 Built with AI
This project was **designed and directed by Caleb (CaLabs)** and **built with the help of AI**: the code and documentation were written together with **Claude**, an AI assistant made by Anthropic. Every feature is tested and reviewed by a human.

> ✍️ *Co-created with Claude (Anthropic)* 🧡

---

## 🇨🇱 Español

CaLabs Playout es un **prototipo de playout para televisión en un solo archivo HTML**. Una alternativa moderna a software de emisión antiguo, caro y poco intuitivo, ideal para personas que están entrando al mundo audiovisual en la programación e ingesta de material para TV.

> ⚠️ **Nota de idioma:** por ahora la interfaz está **solo en español**. El soporte de varios idiomas (incluido inglés) **está planificado y en desarrollo**. 🌍

### ✨ Qué hace hoy
- 🗓️ Parrilla con contador de horas cargadas y duración meta libre
- ✂️ Cortes *dentro* de un programa, con grupos de spots guardados
- 📢 Tandas y eventos a hora fija
- 🖥️ Ventana de salida aparte (y salidas extra), con cambio sin cortes entre videos
- 🎨 Gráficas en pantalla: mosca (logo) con efectos, hora, zócalo y ticker
- 🔊 Vúmetro y volumen separado de salida y de monitoreo
- 🧩 Paneles redimensionables y plantillas guardadas

| Tandas | Gráficas | Ticker |
|:--:|:--:|:--:|
| ![Tandas](docs/03_breaks.png) | ![Gráficas](docs/04_graphics.png) | ![Ticker](docs/05_ticker.png) |

### 🚀 Cómo probarlo
1. Descarga o clona este repositorio.
2. Haz doble clic en `Abrir CaLabs Playout.bat` (Windows, usa Chrome o Edge), o abre `CaLabs_Playout.html` en Chrome.
3. Carga tus propios videos MP4 desde la pestaña Biblioteca.
4. ¿Tienes MPG, AVI o WMV? Usa `Convertir MPG a MP4.bat` (requiere FFmpeg).

### 🧱 Versión nativa 2.0 — CasparCG + NDI (en desarrollo)

Una reescritura como **programa nativo de Windows** (C# / .NET 10) sobre el motor **CasparCG 2.5**, con **salida NDI**. El prototipo de navegador de arriba se queda como está; el código nuevo está en [`v2/`](v2/).

**✅ Novedades de la 2.0**
- 🛡️ Hecha para no caerse del aire: motor, programador y ventana de control son **programas independientes**. Un supervisor reinicia lo que se congele o muera (motor, programador, ventana), y la imagen sigue saliendo aunque se cierre la ventana.
- 🚨 Playlist de emergencia que entra sola si el video deja de avanzar; la vuelta a la parrilla es manual (*ahora* o *cuando termine el video de emergencia*).
- ✂️ Cortes **dentro** de un programa (el programa sigue exactamente donde quedó), "Tanda ahora" (*ahora* / *al terminar el programa*) y tandas a hora fija, todo lo lleva el motor aunque la ventana esté cerrada.
- 🎨 Gráficas al aire (mosca con efectos, hora, zócalo y ticker) dibujadas por el motor, así salen por la **pantalla y por NDI**. Reglas incluidas: sin gráficas en spots ni videos muy cortos, solo el logo en la emergencia, el ticker y el zócalo se turnan.
- 🔀 Fundido opcional entre videos y opción de "saltar el negro y el silencio al final del programa".
- 🖥️ Salida a pantalla completa en cualquier monitor, atajo de salida de emergencia (`Ctrl+Alt+V`) e icono en la bandeja.
- 🔊 Vúmetro y volumen reales del motor, dispositivo de audio del aire fijo (con aviso en rojo si falta) y salida privada de "escucha".
- 💾 Todo lo que guardas se copia también a un archivo con respaldos automáticos, y se recupera si se borra la memoria del programa.
- 📦 Instalador de Windows con términos y condiciones, elección de carpeta y arranque con Windows (ver [`v2/Instalador`](v2/Instalador)).

**🔜 Lo que falta**
- [ ] 🔄 Botón "Buscar actualización" y ventana de "novedades"
- [ ] 🧰 Script de ajustes de Windows de un clic (que no se duerma, etc.)
- [ ] 📲 Avisos por Telegram a un grupo, con botón "Lo tomo yo"
- [ ] 📹 Entradas en vivo: capturadoras, ATEM Mini, transmisiones web y entrada NDI, con vista previa y un "En directo" manual (sin automatización mientras hay señal en vivo)
- [ ] 🕵️ Vigilante opcional de negro y silencio
- [ ] 📄 Exportar a PDF y pruebas largas de estabilidad

**🛠️ Cómo compilarla** — necesitas el [SDK de .NET 10](https://dotnet.microsoft.com/) y la versión 2.5.1 de CasparCG (se descarga de su página oficial; **no** se incluye aquí). La salida NDI necesita [NDI Tools](https://ndi.video/). Para armar el instalador además hace falta [Inno Setup 6](https://jrsoftware.org/isinfo.php): ejecuta `v2/Instalador/construir.ps1 -Motor "<ruta a CasparCG>"`.

### 🗺️ Próximas actualizaciones

**🔜 Siguiente versión (v3.5)**
- [ ] 🎨 Temas de color: Día / Noche / Neutro (siempre con la paleta CaLabs) y un tema Personalizado con colores libres
- [ ] 🌍 Cambio de idioma (español / inglés)
- [ ] 📊 Vúmetro clásico verde-amarillo-rojo con escala en dB
- [ ] 💾 Grupos de spots: ver qué videos contiene cada grupo guardado y volver a cargarlos
- [ ] 🔀 Fundido más suave entre videos (corrección en curso)
- [ ] 🧱 Zócalo: separación ajustable entre barras, posición independiente o en conjunto y colores propios
- [ ] 🖱️ Doble clic en los divisores de paneles para restablecer su tamaño
- [ ] 🎧 Selección de dispositivos de salida de audio

**🛰️ Más adelante**
- [ ] 🔌 Versión final sobre CasparCG con salida NDI
- [ ] 📹 Entradas en vivo (captura HDMI, NDI, ATEM, OBS)
- [ ] 🌐 Conexión a transmisión externa y salida limpia adicional
- [ ] 💿 Instalador para Windows

> 💡 ¿Ideas o comentarios? Abre un issue. 🙌

### 🤖 Creado con IA
Este proyecto fue **diseñado y dirigido por Caleb (CaLabs)** y **creado con ayuda de inteligencia artificial**: el código y la documentación se escribieron junto a **Claude**, un asistente de IA de Anthropic. Cada función es probada y revisada por una persona.

> ✍️ *Co-creado con Claude (Anthropic)* 🧡

---

<div align="center">

Made with 💙 and 🤖 AI by **CaLabs** · All rights reserved © CaLabs · *Todos los derechos reservados*

</div>
