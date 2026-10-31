// Tela do cliente da Central Biométrica (opção A · Rastreio).
// O programa manda o estado pronto (JSON) pelo WebView2; esta página só desenha.
// Todo texto entra com textContent: nome e empresa vêm de cadastros digitados por pessoas.
(() => {
  'use strict';
  const tela = document.getElementById('tela');
  if (!tela) return;
  const reduz = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const FASES = new Set(['espera', 'lendo', 'ok', 'negado', 'horario', 'cadastro', 'cadastro-ok', 'indisponivel']);
  const PASSOS = new Set(['', 'ativo', 'feito', 'erro']);
  const LEITURAS = new Set(['', 'ok', 'agora', 'erro']);

  // Digital desenhada (não é a digital de ninguém): laços ovais abertos embaixo, com algumas falhas.
  function cristas() {
    const p = [], cx = 60, cy = 70;
    let s = 11;
    const rnd = () => { s = (s * 9301 + 49297) % 233280; return s / 233280; };
    p.push({ d: `M${cx - 3.5} 80L${cx - 3.5} ${cy}A3.5 6 0 0 1 ${cx + 3.5} ${cy}L${cx + 3.5} 76`, da: '' });
    for (let i = 1; i < 10; i++) {
      const rx = 3.5 + i * 5.4, ry = 6 + i * 6.9, a0 = (214 + i * 1.5) * Math.PI / 180, a1 = (-34 - i * 1.5) * Math.PI / 180;
      const x0 = cx + rx * Math.cos(a0), y0 = cy - ry * Math.sin(a0), x1 = cx + rx * Math.cos(a1), y1 = cy - ry * Math.sin(a1);
      const a = 8 + rnd() * 24, b = a + 10 + rnd() * 30, c = b + 8 + rnd() * 24;
      p.push({
        d: `M${x0.toFixed(1)} ${y0.toFixed(1)}A${rx.toFixed(1)} ${ry.toFixed(1)} 0 1 1 ${x1.toFixed(1)} ${y1.toFixed(1)}`,
        da: i < 2 ? '' : `${a.toFixed(1)} 3 ${(b - a).toFixed(1)} 2.6 ${(c - b).toFixed(1)} 3 100 0`
      });
    }
    return p;
  }
  const CR = cristas();
  const caminhos = (w) => CR.map(c => `<path d="${c.d}" stroke-width="${w}"${c.da ? ` pathLength="100" stroke-dasharray="${c.da}"` : ''}/>`).join('');
  const V = '<svg class="v" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5" fill="none" stroke="currentColor" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  const X = '<svg class="x" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 7l10 10M17 7L7 17" fill="none" stroke="currentColor" stroke-width="3.2" stroke-linecap="round"/></svg>';
  const ETAPA = `<li><span class="t-bola">${V}${X}</span><span class="t-et-txt"></span></li>`;

  // Estrutura fixa, sem nenhum dado de pessoa.
  tela.innerHTML = `<div class="t-in">
  <div class="t-topo"><img class="t-logo" src="assets/Central.png" alt="Central — Future Place"><span class="t-marca">Future Place<br>Coworking</span><span class="t-hora" data-hora></span></div>
  <div class="t-palco">
    <div class="t-dig">
      <svg viewBox="0 0 120 140" role="img" aria-label="Digital">
        <defs><clipPath id="cl"><rect data-enche x="-10" y="145" width="140" height="170"/></clipPath>
        <linearGradient id="sc" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="#6fb6ff" stop-opacity="0"/><stop offset=".5" stop-color="#6fb6ff" stop-opacity=".9"/><stop offset="1" stop-color="#6fb6ff" stop-opacity="0"/></linearGradient></defs>
        <g class="base">${caminhos(2.6)}</g>
        <g class="cheio" clip-path="url(#cl)">${caminhos(2.9)}</g>
        <rect class="scan" x="4" y="2" width="112" height="6" rx="3" fill="url(#sc)"/>
        <g class="marca-ok"><circle cx="92" cy="112" r="17" fill="#1a7f4b" stroke="#fff" stroke-width="3"/><path d="M84 112l5.5 5.5L101 106" fill="none" stroke="#fff" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"/></g>
        <g class="marca-nao"><circle cx="92" cy="112" r="17" fill="#b26a00" stroke="#fff" stroke-width="3"/><path d="M85.5 105.5l13 13M98.5 105.5l-13 13" fill="none" stroke="#fff" stroke-width="4" stroke-linecap="round"/></g>
      </svg></div>
    <div class="t-txts"><p class="t-rot" data-rot></p><h1 class="t-tit" data-tit></h1><p class="t-txt" data-txt></p><p class="t-sala" data-sala></p></div>
    <div class="t-leit"><div class="t-leit-pts"></div><b class="t-leit-n" data-leitn></b></div>
    <ol class="t-etapas">${ETAPA}${ETAPA}${ETAPA}${ETAPA}</ol>
  </div>
  <section class="t-cartao" aria-label="Cartão de quem está entrando">
    <div class="c-topo"><img src="assets/Central.png" alt=""><span>FUTURE PLACE COWORKING</span></div>
    <div class="c-corpo"><div class="c-foto" aria-hidden="true"></div>
      <div class="c-dados"><b class="c-nome"></b><span class="c-emp"></span></div></div>
    <dl class="c-dl"></dl>
    <div class="c-pe"><span class="c-selo"><svg class="v" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5" fill="none" stroke="currentColor" stroke-width="3.4" stroke-linecap="round" stroke-linejoin="round"/></svg><svg class="a" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 6.5v7M12 17.5v.2" fill="none" stroke="currentColor" stroke-width="3.4" stroke-linecap="round"/></svg><span data-selo></span></span></div>
  </section>
  <div class="t-seta"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 4v14M5.5 12l6.5 6.5 6.5-6.5" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/></svg><span>O leitor fica logo abaixo desta tela</span></div>
  <p class="t-rodape">Precisa de ajuda? Fale com a recepção.</p>
</div>`;

  const $ = (seletor) => tela.querySelector(seletor);
  const el = {
    rot: $('[data-rot]'), tit: $('[data-tit]'), txt: $('[data-txt]'), sala: $('[data-sala]'), leitN: $('[data-leitn]'),
    pontos: $('.t-leit-pts'), etapas: Array.from(tela.querySelectorAll('.t-etapas li')), cartao: $('.t-cartao'),
    foto: $('.c-foto'), nome: $('.c-nome'), empresa: $('.c-emp'), dl: $('.c-dl'), selo: $('.c-selo'), seloTxt: $('[data-selo]'),
    enche: $('[data-enche]'), hora: $('[data-hora]')
  };
  const texto = (no, valor) => { no.textContent = typeof valor === 'string' ? valor : ''; };
  let chave = '';
  let timers = [];
  let enche = 0, alvo = 0, pintando = false;

  function titulo(valor, destaque) {
    const t = typeof valor === 'string' ? valor : '';
    const d = typeof destaque === 'string' ? destaque : '';
    const i = d ? t.indexOf(d) : -1;
    if (i < 0) { el.tit.textContent = t; return; }
    const em = document.createElement('em');
    em.textContent = d;
    el.tit.replaceChildren(t.slice(0, i), em, t.slice(i + d.length));
  }

  // Cada ✓ novo entra um depois do outro, como na entrega acompanhada.
  function passos(lista, etapas, erroTxt) {
    timers.forEach(clearTimeout);
    timers = [];
    let atraso = 0;
    el.etapas.forEach((li, i) => {
      const novo = Array.isArray(lista) && PASSOS.has(lista[i]) ? lista[i] : '';
      const rotulo = Array.isArray(etapas) && typeof etapas[i] === 'string' ? etapas[i] : '';
      texto(li.querySelector('.t-et-txt'), novo === 'erro' && typeof erroTxt === 'string' && erroTxt ? erroTxt : rotulo);
      const aplica = () => { if ((li.dataset.e || '') !== novo) li.dataset.e = novo; };
      const espera = atraso;
      if (novo === 'feito' && li.dataset.e !== 'feito' && !reduz) atraso += 260;
      if (espera === 0) aplica(); else timers.push(setTimeout(aplica, espera));
    });
  }

  function leituras(lista) {
    const valores = Array.isArray(lista) ? lista.slice(0, 10) : [];
    while (el.pontos.children.length < valores.length) el.pontos.append(document.createElement('i'));
    while (el.pontos.children.length > valores.length) el.pontos.lastElementChild.remove();
    Array.from(el.pontos.children).forEach((ponto, i) => {
      const v = LEITURAS.has(valores[i]) ? valores[i] : '';
      if ((ponto.dataset.l || '') !== v) ponto.dataset.l = v;
    });
  }

  function cartao(c) {
    const tem = !!(c && typeof c === 'object' && typeof c.nome === 'string' && c.nome);
    el.cartao.classList.toggle('tem', tem);
    if (!tem) return;
    texto(el.nome, c.nome);
    texto(el.empresa, c.empresa);
    el.empresa.hidden = !c.empresa;
    el.foto.replaceChildren(document.createTextNode(typeof c.iniciais === 'string' ? c.iniciais : ''));
    // Foto só do armazenamento do cadastro; se não carregar, ficam as iniciais.
    if (typeof c.foto === 'string' && c.foto.startsWith('https://storage.googleapis.com/')) {
      const img = new Image();
      img.alt = '';
      img.decoding = 'async';
      img.referrerPolicy = 'no-referrer';
      img.addEventListener('error', () => img.remove());
      img.src = c.foto;
      el.foto.append(img);
    }
    const itens = [['ID do cliente', c.codigo], ['Sala', c.sala], ['Validade', c.validade]].filter(([, v]) => typeof v === 'string' && v);
    el.dl.replaceChildren(...itens.map(([rotulo, valor]) => {
      const item = document.createElement('div');
      const dt = document.createElement('dt');
      const dd = document.createElement('dd');
      dt.textContent = rotulo;
      dd.textContent = valor;
      item.append(dt, dd);
      return item;
    }));
    el.selo.classList.toggle('alerta', c.alerta === true);
    texto(el.seloTxt, typeof c.selo === 'string' && c.selo ? c.selo : 'Identificação confirmada');
  }

  function pinta() {
    enche += (alvo - enche) * (reduz ? 1 : 0.09);
    if (Math.abs(alvo - enche) < 0.002) enche = alvo;
    el.enche.setAttribute('y', ((1 - enche) * 150 - 5).toFixed(2));
    if (enche === alvo) { pintando = false; return; }
    requestAnimationFrame(pinta);
  }
  function enchePara(valor) {
    const v = Number(valor);
    alvo = Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0;
    if (!pintando) { pintando = true; requestAnimationFrame(pinta); }
  }

  function render(st) {
    if (!st || typeof st !== 'object' || Array.isArray(st)) return;
    const k = JSON.stringify(st);
    if (k === chave) return;
    chave = k;
    tela.dataset.fase = FASES.has(st.fase) ? st.fase : 'espera';
    tela.dataset.ped = st.ped === 'poe' ? 'poe' : '';
    tela.dataset.falhou = st.falhou === true ? 'sim' : '';
    texto(el.rot, st.rot);
    titulo(st.tit, st.destaque);
    texto(el.txt, st.txt);
    texto(el.sala, st.sala);
    el.sala.classList.toggle('tem', typeof st.sala === 'string' && st.sala.length > 0);
    texto(el.leitN, st.leitN);
    passos(st.passos, st.etapas, st.erroTxt);
    leituras(st.leit);
    cartao(st.cartao);
    enchePara(st.enche);
  }

  const relogio = (() => {
    try {
      const formato = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', hour: '2-digit', minute: '2-digit' });
      return () => { el.hora.textContent = formato.format(new Date()); };
    } catch (e) {
      return () => { el.hora.textContent = ''; };
    }
  })();
  relogio();
  setInterval(relogio, 1000);

  render({ fase: 'espera', rot: 'Bem-vindo à Future Place', tit: 'Só um instante', txt: 'Preparando o leitor.' });
  const ponte = window.chrome && window.chrome.webview;
  if (ponte) {
    ponte.addEventListener('message', (evento) => render(evento.data));
    ponte.postMessage('pronto');
  }
  // Para conferência fora do programa: window.telaCliente({...}) desenha um estado.
  window.telaCliente = render;
})();
