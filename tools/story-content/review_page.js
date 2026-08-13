// Behaviour for the Armenian text review page. A REAL file, read verbatim by
// build_review_page.py, because the first version lived as a Python string
// inside another Python string: `\n` had to survive two layers of quoting to
// come out as two characters, it survived one, and arrived as an actual line
// break inside a single-quoted JS string. That is a syntax error, so the whole
// script failed to parse and not one button ever got a listener. The owner
// tapped them and nothing happened.
//
// Two rules follow from that, and they are why this file looks the way it does:
// nothing here is ever re-quoted by the generator, and no backslash escape is
// relied upon anywhere in it.

// The newline, without a backslash. Even if this file is ever inlined into
// another language's string literal again, this cannot be corrupted.
var NL = String.fromCharCode(10);
var KEY = 'areg-text-review-v1';

var store = {};
try { store = JSON.parse(localStorage.getItem(KEY) || '{}'); } catch (e) { store = {}; }

var cards = [].slice.call(document.querySelectorAll('.card'));

function save() {
  // A phone in private mode throws here. Losing the marks is bad; losing the
  // buttons as well would be worse, so this never propagates.
  try { localStorage.setItem(KEY, JSON.stringify(store)); } catch (e) {}
}

function say(message) {
  document.getElementById('said').textContent = message;
}

function tally() {
  var done = 0, changed = 0;
  cards.forEach(function (card) {
    var s = store[card.dataset.k];
    if (s && s.s) { done++; if (s.s === 'no') changed++; }
  });
  document.getElementById('done').textContent = done;
  document.getElementById('changed').textContent = changed ? changed + ' փոխելու' : '';
  document.getElementById('fill').style.width = (done / cards.length * 100) + '%';
}

function paint(card) {
  var s = store[card.dataset.k] || {};
  if (s.s) { card.dataset.s = s.s; } else { card.removeAttribute('data-s'); }
  var box = card.querySelector('.fix');
  if (s.fix != null) { box.value = s.fix; }
  card.querySelector('.ok').setAttribute('aria-pressed', s.s === 'ok' ? 'true' : 'false');
  card.querySelector('.no').setAttribute('aria-pressed', s.s === 'no' ? 'true' : 'false');
}

function mark(card, value) {
  var s = store[card.dataset.k] || {};
  s.s = (s.s === value) ? null : value;      // tapping the same button unmarks
  store[card.dataset.k] = s;
  paint(card);
  save();
  tally();
  return s.s;
}

cards.forEach(function (card) {
  paint(card);
  card.querySelector('.ok').addEventListener('click', function () {
    mark(card, 'ok');
  });
  card.querySelector('.no').addEventListener('click', function () {
    if (mark(card, 'no') === 'no') {
      card.querySelector('.fix').focus({ preventScroll: true });
    }
  });
  card.querySelector('.fix').addEventListener('input', function (e) {
    var s = store[card.dataset.k] || {};
    s.fix = e.target.value;
    store[card.dataset.k] = s;
    save();
  });
});

tally();

document.getElementById('jump').addEventListener('click', function () {
  var next = cards.filter(function (card) {
    var s = store[card.dataset.k];
    return !(s && s.s);
  })[0];
  if (next) {
    next.scrollIntoView({ block: 'center', behavior: 'smooth' });
    say('');
  } else {
    say('Ամեն տող նայված է։');
  }
});

function buildReport() {
  var blocks = [];
  cards.forEach(function (card) {
    var s = store[card.dataset.k];
    if (!s || s.s !== 'no') { return; }
    blocks.push([
      '#' + card.dataset.n + '  [' + card.dataset.k + ']',
      '  հին՝ ' + card.querySelector('.hy').textContent.trim(),
      '  նոր՝ ' + ((s.fix || '').trim() || '(չգրված)')
    ].join(NL));
  });
  if (!blocks.length) { return null; }
  return 'Areg — փոխելու տողերը (' + blocks.length + '):' + NL + NL
       + blocks.join(NL + NL) + NL;
}

function copyFallback(text) {
  var box = document.createElement('textarea');
  box.value = text;
  box.style.cssText = 'position:fixed;top:0;left:0;opacity:0';
  document.body.appendChild(box);
  box.select();
  var ok = false;
  try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
  document.body.removeChild(box);
  return ok;
}

document.getElementById('copy').addEventListener('click', function () {
  var text = buildReport();
  if (!text) { say('Ոչ մի տող փոխելու նշված չէ։'); return; }
  var ok = function () { say('Պատճենվեց։ Հիմա փակցրու չաթում։'); };
  var no = function () {
    if (copyFallback(text)) { ok(); } else { say('Չստացվեց պատճենել։'); }
  };
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(text).then(ok, no);
  } else {
    no();
  }
});
