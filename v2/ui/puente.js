// CaLabs Playout 2.0 - puente entre la pantalla y el programa nativo.
// La pantalla ya no reproduce nada: arma la lista, la entrega al Programador y muestra lo que el motor informa.
// Regla: nada que decida que sale al aire vive aqui. Si esta ventana se cierra, el aire sigue.
(function () {
  if (!(window.chrome && chrome.webview)) return; // abierta fuera del programa: no hay motor
  var host = function (o) { chrome.webview.postMessage(o); };
  window.addEventListener('error', function (e) { host({ t: 'log', m: 'ERROR ' + e.message + ' (' + String(e.filename).split('/').pop() + ':' + e.lineno + ')' }); }); // los errores de la pantalla quedan en el registro

  /* ---------- vista previa: el Monitor muestra lo que CasparCG tiene al aire ---------- */
  var st = document.createElement('style');
  st.textContent = '#prevStage img.previa{position:absolute;inset:0;width:100%;height:100%;object-fit:contain;z-index:2;pointer-events:none}';
  document.head.appendChild(st);
  var img = document.createElement('img');
  img.className = 'previa'; img.alt = '';
  prev.cont.appendChild(img);

  /* ---------- la parrilla se convierte en una lista plana de tramos para el Programador ---------- */
  var ENT = [], EM = [], porId = new Map(), libLista = false, ultLista = '', ultEm = '';
  function ruta(n) { var f = S.lib.get(n); return f && f.ok && f.ruta ? f.ruta : null; }
  function proximoPrograma(i) { // titulo del programa que sigue (los bloques de spots no cuentan), para el zocalo
    var L = S.playlist.length;
    for (var k = 1; k <= L; k++) {
      var j = i + k, c = j < L ? S.playlist[j] : (S.repetir ? S.playlist[j % L] : null);
      if (!c) return '';
      if (!c.bloque) return c === S.playlist[i] ? '' : c.titulo;
    }
    return '';
  }
  // Cada programa se parte en tramos en sus cortes: tramo, spots del corte, tramo... El motor los emite sin hueco y el programa
  // sigue exactamente donde quedo. Los videos de un bloque de spots y los de un corte salen marcados como spot (sin gráficas).
  function compilar() {
    var o = [];
    S.playlist.forEach(function (it, i) {
      if (it.bloque) {
        (it.clips || []).filter(spotVigente).forEach(function (c, k) {
          var r = ruta(c.archivo); if (r) o.push({ id: it.uid + ':' + k, ruta: r, ini: 0, fin: 0, fila: i, nombre: c.archivo, spot: true, grupo: it.uid, grp: it.uid, gname: it.titulo });
        });
        return;
      }
      var r0 = ruta(it.archivo); if (!r0) return;
      var ini = it.ini || 0, fin = finDe(it), dur = durProg(it), prox = proximoPrograma(i);
      var ne = S.graf.negro ? negros[r0] : 0; if (ne && (!fin || ne < fin)) fin = ne; // negro y silencio al final: el programa termina donde empiezan
      var cortes = (it.cortes || []).filter(function (c) { return c > ini && (!fin || c < fin) && infoCorte(it, c).clips.length; }).sort(function (a, b) { return a - b; });
      var lim = [ini].concat(cortes);
      lim.forEach(function (desde, k) {
        o.push({ id: k ? it.uid + '#' + k : it.uid, ruta: r0, ini: desde, fin: k < cortes.length ? cortes[k] : fin, fila: i, nombre: it.archivo, titulo: it.titulo, proximo: prox, zoc: it.zocalo !== false, grupo: it.uid, dur: dur });
        if (k < cortes.length) {
          var ic = infoCorte(it, cortes[k]);
          ic.clips.forEach(function (n, j) {
            var rr = ruta(n); if (rr) o.push({ id: it.uid + 'c' + k + ':' + j, ruta: rr, ini: 0, fin: 0, fila: i, nombre: n, spot: true, grupo: it.uid, grp: it.uid + 'c' + k, gname: ic.nombre });
          });
        }
      });
    });
    return o;
  }
  function soloMotor(l) { return l.map(function (e) { return { id: e.id, ruta: e.ruta, ini: e.ini, fin: e.fin, spot: !!e.spot, titulo: e.titulo || '', proximo: e.proximo || '', zoc: e.zoc !== false, grupo: e.grupo || '', dur: e.dur || 0 }; }); }
  // tandas a hora fija: las lleva el motor; aqui solo se le entregan con sus videos
  function eventosMotor() {
    return S.eventos.map(function (ev) {
      var t = tandaById(ev.tanda);
      return { id: ev.id, hora: ev.hora, modo: ev.modo, nombre: t ? t.nombre : 'Tanda', clips: clipsValidos(t).map(ruta).filter(Boolean) };
    }).filter(function (ev, k) { return S.tandas.some(function (t) { return t.id === S.eventos[k].tanda; }) && ev.clips.length; }); // una tanda que ya no existe no sale
  }
  // final en negro y sin sonido: el motor (con ffmpeg) revisa los programas de la parrilla y devuelve donde empieza; solo se usa si la opcion esta activada
  var negros = {}, negrosPedidos = {}; window.NEGROS = negros;
  function pedirNegros() {
    if (!S.graf.negro) return;
    var rs = []; ENT.forEach(function (e) { if (!e.spot && !(e.ruta in negros) && !negrosPedidos[e.ruta] && rs.indexOf(e.ruta) < 0) rs.push(e.ruta); });
    if (rs.length) { rs.forEach(function (r) { negrosPedidos[r] = 1; }); host({ t: 'negros', rutas: rs }); }
  }
  function enviarListas() {
    ENT = compilar(); porId = new Map(ENT.map(function (e, j) { return [e.id, j]; }));
    pedirNegros();
    if (!libLista) return; // hasta conocer la biblioteca no se toca la lista del motor (podria estar al aire)
    var l = JSON.stringify({ repetir: S.repetir, entradas: soloMotor(ENT), eventos: eventosMotor() });
    if (l !== ultLista) { ultLista = l; host({ t: 'lista', cual: 'parrilla', json: l }); }
    EM = S.emerg.map(function (n, j) { return { id: 'e' + j + ':' + n, ruta: ruta(n), ini: 0, fin: 0 }; }).filter(function (x) { return x.ruta; });
    var em = JSON.stringify({ repetir: true, entradas: EM });
    if (em !== ultEm) { ultEm = em; host({ t: 'lista', cual: 'emergencia', json: em }); }
  }
  var guardarAuto0 = guardarAuto;
  window.guardarAuto = function () { guardarAuto0(); enviarListas(); enviarGrafica(); };
  setInterval(enviarListas, 60000); // las vigencias de los spots cambian con la fecha

  /* ---------- Tandas comerciales: una sola cosa. Cada tanda guardada en la pestaña es una tanda para «Tanda ahora», Hora fija y los cortes.
     Internamente sigue habiendo S.tandas, pero se arma sola a partir de las tandas guardadas (id 'G:nombre') + la lista cargada ahora ('t1'). ---------- */
  function sincronizarTandas() {
    var viejas = S.tandas, t1 = viejas.find(function (t) { return t.id === 't1'; });
    var legacy = t1 && t1.clips.length && !(t1.clips.length === 1 && t1.clips[0] === SPOTS_TOKEN); // una tanda propia de antes con videos: se conserva
    var nuevas = [legacy ? t1 : { id: 't1', nombre: 'Spots cargados ahora', clips: [SPOTS_TOKEN] }];
    listaGuardada('calabs_paquetes').forEach(function (n) { nuevas.push({ id: 'G:' + n, nombre: n, clips: ['G:' + n] }); });
    viejas.forEach(function (t) { if (t.id !== 't1' && t.id.indexOf('G:') !== 0 && t.clips.length) nuevas.push(t); });
    if (JSON.stringify(nuevas) !== JSON.stringify(viejas)) {
      S.tandas = nuevas; guardarAuto();
      S.eventos.forEach(function (ev) { if (!nuevas.some(function (t) { return t.id === ev.tanda; })) registrar('AVISO', 'La tanda de las ' + ev.hora.slice(0, 5) + ' ya no existe: no va a salir'); });
    }
    if (!S.tandas.some(function (t) { return t.id === tandaSel; })) tandaSel = 't1';
  }
  var rt0 = renderTandas;
  window.renderTandas = function () {
    rt0();
    var sel = $('#selTandaAhora');
    sel.innerHTML = S.tandas.map(function (t) { return '<option value="' + esc(t.id) + '"' + (t.id === tandaSel ? ' selected' : '') + '>' + esc(t.nombre) + '</option>'; }).join('');
  };
  $('#selTandaAhora').onchange = function (e) { tandaSel = e.target.value; };
  ['guardarEn', 'borrarDe', 'aplicar'].forEach(function (f) {
    var o = window[f]; window[f] = function () { var r = o.apply(this, arguments); sincronizarTandas(); renderTandas(); renderEventos(); enviarListas(); return r; };
  });
  sincronizarTandas(); renderTandas(); renderEventos();

  /* ---------- copia en archivo de lo que se guarda (tandas, plantillas, parrilla...): datos\guardados.json + respaldos ---------- */
  var copiaLista = false, tCopia;
  function claveCopia(k) { return String(k).indexOf('calabs_') === 0 && !/geo|aparte/.test(k); }
  function programarCopia() {
    if (!copiaLista) return;
    clearTimeout(tCopia);
    tCopia = setTimeout(function () {
      var o = {}; for (var i = 0; i < localStorage.length; i++) { var k = localStorage.key(i); if (claveCopia(k)) o[k] = localStorage.getItem(k); }
      host({ t: 'guardados', json: JSON.stringify(o) });
    }, 1500);
  }
  var setI0 = Storage.prototype.setItem, remI0 = Storage.prototype.removeItem, restaurando = false;
  Storage.prototype.setItem = function (k, v) { if (restaurando && this === localStorage) return; setI0.call(this, k, v); if (this === localStorage && claveCopia(k)) programarCopia(); };
  Storage.prototype.removeItem = function (k) { remI0.call(this, k); if (this === localStorage && claveCopia(k)) programarCopia(); };
  function alGuardados(d) {
    var yaRecargo = sessionStorage.getItem('calabs_restaurado');
    sessionStorage.removeItem('calabs_restaurado');
    var o = {}; try { o = d.json ? JSON.parse(d.json) : {}; } catch (x) { host({ t: 'log', m: 'La copia guardada no se pudo leer: ' + x.message }); }
    var n = 0;
    if (!yaRecargo) Object.keys(o).forEach(function (k) {
      // memoria nueva (se borro): se recupera todo. Si no, solo lo que falta, sin tocar la parrilla de trabajo.
      if (d.nuevo || (localStorage.getItem(k) === null && k !== 'calabs_actual')) { setI0.call(localStorage, k, o[k]); n++; }
    });
    if (n) {
      host({ t: 'log', m: 'Se recuperaron ' + n + ' dato(s) guardados desde datos\\guardados.json' });
      restaurando = true; // al recargar la pagina guarda su estado vacio: no se deja
      window.addEventListener('beforeunload', function (e) { e.stopImmediatePropagation(); }, true); // y no pregunta "¿salir?" aunque haya aire
      sessionStorage.setItem('calabs_restaurado', '1'); location.reload(); return;
    }
    copiaLista = true; programarCopia();
  }

  /* ---------- graficas al aire: logo, reloj, zocalo y ticker los dibuja el motor (capa 30) y salen por la pantalla completa y por NDI.
     Aqui solo se deja su configuracion y se avisa de las ordenes manuales; el Monitor muestra lo que el motor dibuja de verdad. ---------- */
  var stG = document.createElement('style');
  stG.textContent = '#prevStage .zocalo,#prevStage .ticker{visibility:hidden!important}#prevStage .mosca *{visibility:hidden!important}#prevStage .hora{color:transparent!important;background:transparent!important;text-shadow:none!important}';
  document.head.appendChild(stG); // los dibujos propios del Monitor quedan invisibles pero se pueden arrastrar para fijar su lugar
  var ultG = '', rutaLogo = '';
  function enviarGrafica() {
    if (!libLista) return;
    var u = S.graf.logoSel === 'archivo' ? rutaLogo : decodeURI(LOGOS[S.graf.logoSel] || LOGOS.calabs).replace(location.href.replace(/[^/]*$/, ''), '');
    var j = JSON.stringify({ g: S.graf, logo: u });
    if (j !== ultG) { ultG = j; host({ t: 'grafica', json: j }); }
  }
  function evento(fn, o) { host({ t: 'evento', fn: fn, json: JSON.stringify(o || {}) }); }
  var ag0 = actualizarGraficas;
  window.actualizarGraficas = function () { ag0(); enviarGrafica(); };
  var at0 = armarTicker;
  window.armarTicker = function (st, t) {
    at0(st, t);
    if (st === prev) evento('tk', { texto: tkTxt(t), y: t.y, tam: t.tam, fuente: t.fuente, negrita: t.negrita, color: t.color, fondo: t.fondo, colorFondo: t.colorFondo, opFondo: t.opFondo, vel: t.vel });
  };
  var to0 = tickerOff;
  window.tickerOff = function (fin) { to0(fin); evento('tkOff'); };
  var fxI2 = 0; // "Probar" efectos del logo: se manda el siguiente de los marcados a la capa de graficas del motor
  window.animarMosca = function () {
    var l = S.graf.fx.filter(function (x) { return FX[x]; }); if (!l.length) return;
    if (!S.graf.mosca) { toast('Activa «Mostrar mosca» para ver el efecto'); return; }
    evento('fx', { n: l[fxI2++ % l.length] });
  };
  window.mostrarZocalo = function (forzar) { // "Probar zócalo": el automatico lo maneja el programa
    var it = S.playlist[E.idx] || S.playlist.find(function (x) { return !x.bloque; }); if (!it || it.bloque) return;
    var i = S.playlist.indexOf(it);
    evento('zoc', { a: it.titulo, b: proximoPrograma(i) });
  };
  window.ocultarZocalo = function () {};
  $('#logoIn').addEventListener('change', function (e) { // el motor necesita el archivo en disco: el programa lo copia a datos
    var f = e.target.files[0]; if (f) chrome.webview.postMessageWithAdditionalObjects({ t: 'logo' }, [f]);
  }, true);

  /* ---------- audio: el volumen lo aplica el motor a toda la salida (pantalla y NDI) y el vumetro mide lo que el motor emite ---------- */
  var dbL = -90, dbR = -90;
  window.aplicarVol = function () { host({ t: 'volumen', v: volAire() }); };
  window.pasoVU = function () {
    var a = nivelVU(Math.pow(10, dbL / 20) / 1.41), b = nivelVU(Math.pow(10, dbR / 20) / 1.41); // la escala ya espera un nivel RMS
    vuL = a > vuL ? a : Math.max(a, vuL - 3); vuR = b > vuR ? b : Math.max(b, vuR - 3);
    $('#vuL').style.setProperty('--lv', vuL.toFixed(1) + '%'); $('#vuR').style.setProperty('--lv', vuR.toFixed(1) + '%');
    pasoVU.n = (pasoVU.n || 0) + 1;
    if (pasoVU.n % 8 === 0) { var p = Math.max(vuL, vuR), el = $('#vuDb'); el.textContent = p > 0.5 ? (p / 100 * 54 - 54).toFixed(0) + ' dB' : '−∞ dB'; el.style.color = p > 88.9 ? 'var(--rojo)' : p > 66.7 ? '#f5d524' : ''; }
  };
  // dispositivos: "Al aire sale por" lo aplica el motor (reinicia); "Mi escucha" repite esa salida por otro dispositivo con su propio volumen
  var disp = { salidas: [], aire: '' };
  function llenarDisp() {
    var op = function (sel) { return '<option value="">Predeterminado de Windows</option>' + (sel && disp.salidas.indexOf(sel) < 0 ? '<option value="' + esc(sel) + '" selected>⚠ ' + esc(sel) + ' (no conectado)</option>' : '') + disp.salidas.map(function (n) { return '<option value="' + esc(n) + '"' + (n === sel ? ' selected' : '') + '>' + esc(n) + '</option>'; }).join(''); };
    $('#auAire').innerHTML = op(disp.aire); $('#auEscDev').innerHTML = op(AU.escN || '');
  }
  function escuchaMandar() { host({ t: 'escucha', on: AU.escOn, destino: AU.escN || '', origen: disp.aire, vol: AU.esc / 100 }); }
  $('#auDispTxt').textContent = 'Al aire sale por el dispositivo elegido (reinicia el motor: unos segundos sin señal). «Mi escucha» repite esa salida por otro dispositivo con su propio volumen y no cambia lo que sale al aire. Elige dispositivos distintos.';
  $('#auDisp').style.display = ''; $('#auEsc').disabled = false; $('#auEscOn').disabled = false;
  $('#auBuscar').onclick = function () { host({ t: 'audioLista' }); };
  $('#auAire').onchange = function (e) {
    var n = e.target.value;
    if (!confirm('¿Cambiar el dispositivo por donde sale el aire?\n\nEl motor se reinicia y la señal se corta unos segundos.')) { e.target.value = disp.aire; return; }
    disp.aire = n; host({ t: 'audioAire', nombre: n }); registrar('SISTEMA', 'Audio al aire por: ' + (n || 'predeterminado de Windows'));
    if (AU.escOn) setTimeout(escuchaMandar, 500);
  };
  $('#auEscDev').onchange = function (e) { AU.escN = e.target.value; guardarAU(); if (AU.escOn) escuchaMandar(); };
  $('#auEsc').oninput = function (e) { AU.esc = +e.target.value; pintarAudio(); host({ t: 'escucha', soloVolumen: true, vol: AU.esc / 100 }); };
  $('#auEscOn').onclick = function () {
    if (!AU.escOn && !AU.escN) { toast('Elige primero el dispositivo de «Mi escucha» en «Dispositivos de audio»'); return; }
    AU.escOn = !AU.escOn; pintarAudio(); escuchaMandar();
  };
  // "Mi escucha" queda dentro de «Dispositivos de audio» (apagada al abrir): es una herramienta de pruebas, no del aire
  (function () {
    var fila = $('#auEsc').closest('.arow'), caja = $('#auDisp'), c = document.createElement('div');
    c.style.cssText = 'display:flex;gap:8px;align-items:center;margin-top:6px';
    c.innerHTML = '<span title="Repite el aire por el dispositivo de «Mi escucha» con su propio volumen. No cambia lo que sale al aire.">🎧 Mi escucha</span>';
    ['#auEsc', '#auEscN', '#auEscOn'].forEach(function (s) { c.appendChild($(s)); });
    caja.appendChild(c); fila.style.display = 'none';
  })();
  // por donde sale el aire: siempre a la vista, y en rojo si ese dispositivo no esta conectado
  var info = document.createElement('div');
  info.id = 'auAireInfo'; info.style.cssText = 'font-size:11px;padding:2px 10px 4px;line-height:1.3';
  $('#auVol').closest('.arow').after(info);
  function pintarAireInfo(d) {
    var n = d.aire, el = $('#auAireInfo');
    if (d.falta) { el.style.color = 'var(--rojo)'; el.style.fontWeight = '700'; el.textContent = '⚠ El dispositivo del aire no está conectado: ' + n + '. No hay sonido hasta que vuelva.'; }
    else if (n) { el.style.color = 'var(--mut)'; el.style.fontWeight = ''; el.textContent = '🔊 Aire por: ' + n; }
    else { el.style.color = '#f5d524'; el.style.fontWeight = ''; el.textContent = '🔊 Aire por el predeterminado de Windows: puede cambiar si conectan unos audífonos. Fíjalo en «Dispositivos de audio».'; }
  }
  host({ t: 'audioLista' });
  aplicarVol();

  /* ---------- aviso si el motor de video no esta (por ejemplo, se cerro la ventana de salida) ---------- */
  var avisoSM = document.createElement('div');
  avisoSM.id = 'sinSalida'; avisoSM.hidden = true;
  avisoSM.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:50;background:var(--rojo);color:#fff;font-weight:800;text-align:center;padding:8px;font-size:15px';
  avisoSM.textContent = '⚠ NO HAY SALIDA AL AIRE — el motor de video está cerrado. Pulsa «🖥️ Abrir salida».';
  document.body.appendChild(avisoSM);
  var sinMotor = 0;
  setInterval(function () {
    if (M.motor) sinMotor = 0; else if (!sinMotor) sinMotor = Date.now();
    avisoSM.hidden = !(sinMotor && Date.now() - sinMotor > 5000);
  }, 1000);

  /* ---------- estado del motor -> lo que muestra la pantalla ---------- */
  var M = { motor: false }, V = { currentTime: 0, duration: 0 }, ultClave = '', ultId = null, ultEmerg = false, ultOn = false;
  function base(p) { return String(p || '').split(/[\\/]/).pop(); }
  function alEstado(m) {
    M = m;
    var emerg = !!(m.emerg && m.emerg.activa), on = !!m.motor && (emerg || !!m.activo);
    var id0 = String(m.id || '').indexOf('r~') === 0 ? m.id.split('~')[1] : m.id; // 'r~' = el programa retomado despues de una tanda
    var j = porId.has(id0) ? porId.get(id0) : -1, en = j >= 0 ? ENT[j] : null;
    E.volver = false; E.esperando = null; E.cambiando = false;
    // "volver cuando termine el video" lo lleva el supervisor; aqui solo se muestra en que fila va a caer
    E.volverPend = emerg && m.emerg.volverPend && ENT[m.emerg.volverIdx] ? ENT[m.emerg.volverIdx].fila : null;
    if (!on) { E.estado = 'off'; E.fuente = 'parrilla'; E.pausa = false; }
    else if (emerg) { E.estado = 'aire'; E.fuente = 'emerg'; E.pausa = !!m.pausa; E.archivoActual = base(m.fg); E.ini = 0; E.fin = 0; }
    else {
      E.fuente = 'parrilla'; E.pausa = !!m.pausa; E.estado = 'aire';
      if (m.tanda) { // tanda que el motor intercalo (manual o a hora fija): "2 de 5"; la fila resaltada es la del programa interrumpido
        var anc = String(m.tandaAncla || ''), ja = porId.has(anc) ? porId.get(anc) : -1;
        if (ja >= 0) E.idx = ENT[ja].fila;
        E.tandaTipo = 'manual';
        E.estado = 'tanda'; E.archivoActual = base(m.fg); E.ini = 0; E.fin = 0; E.tandaNombre = m.tanda;
        E.cola = []; for (var q = 0; q < m.tandaDe; q++) E.cola.push(q + 1 === m.tandaIdx ? base(m.fg) : '·'); E.colaIdx = m.tandaIdx - 1;
      } else if (en) {
        E.idx = en.fila; E.archivoActual = en.nombre; E.ini = en.ini; E.fin = en.fin;
        if (en.spot) { // spots de un bloque o de un corte: se muestran como tanda, "2 de 5"
          var cl = ENT.filter(function (x) { return x.grp === en.grp; });
          E.estado = 'tanda'; E.cola = cl.map(function (x) { return x.nombre; }); E.colaIdx = cl.indexOf(en); E.tandaNombre = en.gname;
          E.tandaTipo = en.grp === en.grupo ? 'bloque' : 'corte';
        }
      }
    }
    V.currentTime = m.pos || 0; V.duration = m.dur || 0;
    // registro de lo que se observa mientras la ventana esta abierta
    if (on !== ultOn) { registrar('SISTEMA', on ? 'Emisión al aire' : 'Emisión detenida'); ultOn = on; }
    if (emerg !== ultEmerg) { registrar('EMERGENCIA', emerg ? 'Entra la playlist de emergencia (' + (m.emerg.motivo || '') + ')' : 'Vuelve la parrilla'); ultEmerg = emerg; }
    if (on && !emerg && m.id !== ultId) { // el registro dice de donde salio cada spot: bloque, corte de un programa o tanda intercalada
      if (m.tanda) registrar('SPOT', base(m.fg) + '  [tanda "' + m.tanda + '" ' + m.tandaIdx + '/' + m.tandaDe + ']');
      else if (en && en.spot) registrar('SPOT', en.nombre + '  [' + (en.grp === en.grupo ? 'bloque "' + en.gname + '"' : 'corte "' + en.gname + '" de "' + ((S.playlist[en.fila] || {}).titulo || '') + '"') + ']');
      else if (en) { var f = S.playlist[en.fila]; registrar('PROGRAMA', f ? f.titulo + '  [' + en.nombre + ']' : en.nombre); }
    }
    ultId = on && !emerg ? m.id : null;
    $('#bTanda').textContent = m.pend ? '📢 Tanda AHORA' : '📢 Tanda ahora';
    $('#bTanda').title = m.pend ? '"' + m.pend + '" espera a que termine el programa. Otro clic la saca ahora mismo.' : 'Saca la tanda seleccionada en la pestaña Tandas';
    var clave = [E.estado, E.fuente, E.idx, E.colaIdx, E.pausa, E.volverPend, m.pend].join('|');
    if (clave !== ultClave) { ultClave = clave; render(); }
  }
  // el reloj principal ya no maneja reproductores: solo pinta
  window.tick2 = function () {
    var h = hms(new Date());
    $('#reloj').textContent = h;
    stages().forEach(function (s) { s.hora.textContent = h.slice(0, 5); });
    correrPend();
    pintarAlAire(V);
  };

  /* ---------- ordenes al Programador ---------- */
  // en emergencia, Anterior / Siguiente / Reiniciar / Pausa / barra de avance mueven los videos de emergencia, no la parrilla
  function enEm() { return !!(M.emerg && M.emerg.activa); }
  function mando(cmd, arg) { host({ t: 'mando', cual: enEm() ? 'emergencia' : 'parrilla', cmd: cmd, arg: arg || 0 }); }
  function actual() { return enEm() ? EM.findIndex(function (x) { return x.id === M.id; }) : porId.has(M.id) ? porId.get(M.id) : -1; }
  function deFila(i) { return ENT.findIndex(function (x) { return x.fila >= i; }); }
  var SIN_VIDEOS = 'Ningún video de la parrilla está disponible. Revisa que el disco esté conectado o vuelve a agregarlos.';
  window.alAire = function (desde) {
    if (enEm()) { volverAParrilla(desde || 0); return; }
    if (!S.playlist.length) { toast('La parrilla está vacía'); return; }
    enviarListas();
    if (!ENT.length) { toast(SIN_VIDEOS); return; }
    registrar('SISTEMA', 'Inicio de emisión — plantilla "' + S.plantilla + '"');
    mando('aire', Math.max(0, deFila(desde || 0)));
  };
  window.reproducirItem = function (i) {
    if (enEm()) { volverAParrilla(i); return; }
    var j = deFila(i);
    if (j < 0 || ENT[j].fila !== i) { toast('Ese video no está cargado: no puede salir al aire'); return; }
    mando(E.estado === 'off' ? 'aire' : 'ir', j);
  };
  window.parar = function () { if (enEm()) { toast('Hay una emergencia al aire. Vuelve primero a la parrilla.'); return; } mando('parar'); };
  window.saltar = function () {
    var j = actual(); if (E.estado === 'off' || j < 0) return;
    var n = j + 1;
    if (n >= (enEm() ? EM : ENT).length) { if (!enEm() && !S.repetir) { toast('Es el último de la parrilla'); return; } n = 0; }
    registrar('SISTEMA', 'Clip siguiente (manual)'); mando('ir', n);
  };
  window.reiniciar = function () {
    if (E.estado === 'off') return;
    registrar('SISTEMA', 'Clip reiniciado (manual)'); mando('buscar', E.ini || 0);
  };
  window.anterior = function () {
    var j = actual(); if (E.estado === 'off' || j < 0) return;
    var n = j - 1;
    var L = (enEm() ? EM : ENT).length;
    if (n < 0) { if ((enEm() || S.repetir) && L > 1) n = L - 1; else { reiniciar(); return; } }
    registrar('SISTEMA', 'Clip anterior (manual)'); mando('ir', n);
  };
  window.buscar = function (t) { if (E.estado !== 'off') mando('buscar', t); };
  window.pausar = function () {
    if (E.estado === 'off') return;
    if (E.pausa) { mando('reanudar'); return; }
    armar($('#bPausa'), '¿Pausar al aire?', function () { mando('pausa'); });
  };
  // volver: 1er clic = cuando termine el video de emergencia; 2º clic = ahora. Siempre desde el principio, salvo que se elija una fila.
  window.volverAParrilla = function (i) {
    enviarListas();
    if (!ENT.length) { toast(SIN_VIDEOS); return; }
    var j = Math.max(0, deFila(i || 0)), ya = M.emerg.volverPend && M.emerg.volverIdx === j;
    if (!ya) toast('Volverá a la parrilla (#' + (ENT[j].fila + 1) + ') cuando termine este video. Presiona otra vez para volver ahora mismo.');
    registrar('SISTEMA', ya ? 'Vuelve a la parrilla (inmediato)' : 'Volverá a la parrilla cuando termine el video de emergencia');
    host({ t: 'orden', cmd: (ya ? 'VOLVER ' : 'VOLVER AL TERMINAR ') + j });
  };
  window.botonEmerg = function () {
    if (enEm()) volverAParrilla(E.volverPend != null ? E.volverPend : 0);
    else host({ t: 'orden', cmd: 'EMERGENCIA' });
  };
  // Tanda ahora: 1er clic = sale cuando termine el programa (con sus cortes); 2º clic = sale ya y el programa se retoma donde iba
  window.iniciarTanda = function (tid) {
    if (enEm()) { toast('Hay una emergencia al aire. Vuelve primero a la parrilla.'); return false; }
    if (E.estado === 'off') { toast('Pon ▶ Al aire primero'); return false; }
    if (E.estado === 'tanda') { toast('Ya hay una tanda en curso'); return false; }
    var t = tandaById(tid), cl = clipsValidos(t).map(ruta).filter(Boolean);
    if (!cl.length) { toast('"' + (t ? t.nombre : 'Tanda') + '" no tiene videos cargados: no salió'); return false; }
    var ya = !!M.pend;
    host({ t: 'mando', cmd: 'tanda', cual: 'parrilla', modo: ya ? 'ahora' : 'terminar', nombre: t.nombre, clips: cl });
    registrar('TANDA', t.nombre + (ya ? ' (ahora)' : ' (cuando termine el programa)'));
    if (!ya) toast('La tanda "' + t.nombre + '" saldrá cuando termine este programa. Presiona otra vez para sacarla ahora mismo.');
    return true;
  };
  window.iniciarClips = function () { return false; };
  $('#bTanda').onclick = function () { iniciarTanda(tandaSel); };
  // salida al aire: en que monitor muestra CasparCG la senal a pantalla completa (o en una ventana, para pruebas)
  var selS = document.createElement('select');
  selS.id = 'selSalida'; selS.title = 'Dónde se ve la salida al aire. Cambiarla reinicia el motor: unos segundos sin señal.';
  var salida = 0, bCerrarS = document.createElement('button');
  bCerrarS.id = 'bCerrarSalida'; bCerrarS.textContent = '✕ Cerrar pantalla completa'; bCerrarS.title = 'La salida vuelve a una ventana pequeña. También con Ctrl+Alt+V desde cualquier parte.';
  var bAbrirS = document.createElement('button');
  bAbrirS.id = 'bAbrirSalida'; bAbrirS.textContent = '🖥️ Abrir salida'; bAbrirS.title = 'Vuelve a abrir la ventana de salida si se cerró. El motor se reinicia: unos segundos sin señal.';
  bAbrirS.onclick = function () { toast('Abriendo la salida… unos segundos sin señal'); host({ t: 'orden', cmd: 'ABRIR' }); };
  var bSalir = document.createElement('button');
  bSalir.id = 'bSalir'; bSalir.className = 'rojo'; bSalir.textContent = '⏻ Salir'; bSalir.title = 'Apaga todo el playout (motor, programador y supervisor). El canal se queda sin señal.';
  bSalir.onclick = function () { armar(bSalir, '¡Se apaga todo! clic otra vez', function () { host({ t: 'orden', cmd: 'CERRAR' }); }); };
  $('#bSalida').replaceWith(selS); selS.after(bAbrirS); bAbrirS.after(bCerrarS); $('#bParar').after(bSalir);
  var hintS = document.createElement('span');
  hintS.className = 'mut'; hintS.style.cssText = 'font-size:11px;white-space:nowrap'; hintS.textContent = '⌨ Cerrar pantalla completa: Ctrl+Alt+V';
  bCerrarS.after(hintS);
  function alSalida(d) { // solo se ofrecen las pantallas que estan conectadas ahora
    salida = d.n;
    selS.innerHTML = '<option value="0">🖥️ Salida: ventana</option>' + d.pantallas.map(function (p) { return '<option value="' + p.n + '">🖥️ Salida: pantalla ' + p.n + ' completa (' + p.an + '×' + p.al + ')</option>'; }).join('');
    selS.value = d.n; bCerrarS.hidden = !d.n;
  }
  function pedirSalida(n) { host({ t: 'orden', cmd: 'SALIDA ' + n }); }
  selS.onchange = function () {
    var n = +selS.value;
    if (!n || confirm('¿Sacar el aire a pantalla completa en la pantalla ' + n + '?\n\n' + (n === 1 ? '⚠ La pantalla 1 es donde está esta ventana: la va a tapar.\n' : '') + 'La señal se corta unos segundos.\nPara cerrar la pantalla completa: las teclas Ctrl+Alt+V (desde cualquier parte) o el botón "✕ Cerrar pantalla completa".')) pedirSalida(n);
    else selS.value = salida;
  };
  bCerrarS.onclick = function () { pedirSalida(0); };
  // las confirmaciones de dos clics ("¿Seguro?") se ven: el boton parpadea en rojo mientras espera el segundo clic
  var st2 = document.createElement('style');
  st2.textContent = '.armado{background:var(--rojo)!important;color:#fff!important;outline:3px solid #fff;animation:armado .4s infinite alternate}@keyframes armado{to{outline-color:var(--rojo)}}';
  document.head.appendChild(st2);
  var armar0 = armar;
  window.armar = function (btn, texto, fn) {
    armar0(btn, texto, fn);
    btn.classList.toggle('armado', !!btn._arm);
    setTimeout(function () { if (!btn._arm) btn.classList.remove('armado'); }, 3100);
  };
  // estos botones guardaron la funcion vieja al cargar la pagina: se les da la nueva
  $('#bSig').onclick = function () { saltar(); }; $('#bAnt').onclick = function () { anterior(); };
  $('#bReini').onclick = function () { reiniciar(); }; $('#bPausa').onclick = function () { pausar(); };
  $('#bEmerg').onclick = $('#bEmerg2').onclick = function () { botonEmerg(); };

  /* ---------- biblioteca: los archivos viven en carpetas reales y el programa las recuerda ---------- */
  var colaF = [], tCola, esperando = new Set();
  window.agregarArchivo = function (file) { // elegidos con un boton: se pide al programa su ruta real y su duracion. Los soltados ya la tienen.
    if (esBasura(file.name)) return;
    if (!S.lib.get(file.name)) S.lib.set(file.name, { nombre: file.name, ok: true, dur: 0, url: null, ruta: '', err: '' });
    esperando.add(file.name);
    if (!(file instanceof File)) return;
    colaF.push(file);
    clearTimeout(tCola);
    tCola = setTimeout(function () { chrome.webview.postMessageWithAdditionalObjects({ t: 'archivos' }, colaF.splice(0)); }, 60);
  };
  // Soltar desde Windows: el programa recibe los archivos o carpetas tal cual, saca su ruta real (de cualquier disco) y devuelve los videos.
  var pendDrop = null;
  window.leerDrop = function (dt) {
    var fs = Array.from(dt.files);
    return new Promise(function (ok) {
      if (!fs.length) { ok([]); return; }
      pendDrop = ok;
      try { chrome.webview.postMessageWithAdditionalObjects({ t: 'archivos', soltar: true }, fs); }
      catch (x) { pendDrop = null; host({ t: 'log', m: 'ERROR al soltar: ' + x.message }); ok([]); return; }
      setTimeout(function () { if (pendDrop === ok) { pendDrop = null; ok([]); } }, 30000);
    });
  };
  function alLib(d) {
    var vistos = new Set();
    d.archivos.forEach(function (f) {
      vistos.add(f.nombre);
      var ya = S.lib.get(f.nombre);
      if (ya) { ya.ok = true; ya.ruta = f.ruta; ya.err = ''; if (f.dur) ya.dur = f.dur; }
      else S.lib.set(f.nombre, { nombre: f.nombre, ok: true, dur: f.dur || 0, url: null, ruta: f.ruta, err: '' });
    });
    if (d.motivo === 'archivos') esperando.clear();
    Array.from(S.lib.keys()).forEach(function (k) { if (!vistos.has(k) && !esperando.has(k)) S.lib.delete(k); });
    libLista = true; renderTodo(); enviarListas(); enviarGrafica();
    if (d.motivo === 'carpeta') toast(d.archivos.length + ' video(s) en la Biblioteca');
    if (d.motivo && d.repetidos.length) toast('Aviso: ' + d.repetidos.length + ' video(s) tienen el mismo nombre que otro ya cargado. Se usa el primero: ' + d.repetidos[0]);
  }
  var quitar0 = quitarDeLib;
  window.quitarDeLib = function (n) { quitar0(n); if (!S.lib.has(n)) { host({ t: 'quitar', nombre: n }); enviarListas(); } };
  $('#bLibLimpiar').onclick = function (ev) {
    if (!S.lib.size) { toast('La lista ya está vacía'); return; }
    if (E.estado !== 'off') { toast('Detén la emisión antes de limpiar la lista'); return; }
    armar(ev.currentTarget, '¿Vaciar la lista?', function () { S.lib.clear(); host({ t: 'limpiar' }); renderTodo(); enviarListas(); toast('Lista vaciada.'); });
  };
  window.abrirCarpeta = function () { host({ t: 'carpeta' }); };
  $('#bCarpeta').onclick = function () { abrirCarpeta(); };

  /* ---------- mensajes del programa ---------- */
  chrome.webview.addEventListener('message', function (e) {
    var d = e.data;
    if (typeof d === 'string') { if (d.indexOf('data:image/jpeg') === 0) img.src = d; return; }
    try {
      if (d.t === 'estado') alEstado(d);
      else if (d.t === 'lib') alLib(d);
      else if (d.t === 'salida') alSalida(d);
      else if (d.t === 'vu') { dbL = d.l; dbR = d.r; }
      else if (d.t === 'negros') { Object.keys(d.datos).forEach(function (k) { negros[k] = d.datos[k]; }); enviarListas(); }
      else if (d.t === 'guardados') alGuardados(d);
      else if (d.t === 'audioEstado') pintarAireInfo(d);
      else if (d.t === 'audioLista') { disp.salidas = d.salidas || []; disp.aire = d.aire || ''; llenarDisp(); }
      else if (d.t === 'escuchaEstado') { if (!d.ok) { AU.escOn = false; pintarAudio(); toast(d.msg || 'No se pudo activar «Mi escucha»'); } }
      else if (d.t === 'logo') { rutaLogo = d.ruta; enviarGrafica(); }
      else if (d.t === 'soltados') { if (pendDrop) { var ok = pendDrop; pendDrop = null; ok(d.nombres.map(function (n) { return { name: n }; })); } }
      else if (d.t === 'respuesta' && String(d.r).indexOf('200') !== 0) toast('No se pudo: ' + String(d.r).replace(/^\d+ /, ''));
    } catch (x) { console.error(x); }
  });
  // el nombre de fabrica anterior de la plantilla pasa al nuevo, salvo que haya una guardada con ese nombre
  if (S.plantilla === 'Día A (Lun-Mié)' && listaGuardada('calabs_plantillas').indexOf(S.plantilla) < 0) { S.plantilla = PLANTILLAS[0]; $('#plantilla').value = S.plantilla; guardarAuto0(); }
  enviarListas();
  host({ t: 'listo' });
})();
