/* Doc kit renderers. DocKit.render(element, type, data) draws one block.
   An element with data-dk="<type>" and data-src="<json url>" is drawn on load.
   Types: tree, columns, graph, stack, folds. A fold with children is a composed fold. */
(function () {
  "use strict";

  // ---- helpers ------------------------------------------------------------
  function esc(s) { return String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); }
  // Inline text: `code` and **bold** only.
  function md(s) {
    return esc(s).replace(/`([^`]+)`/g, "<code>$1</code>").replace(/\*\*([^*]+)\*\*/g, "<b>$1</b>");
  }
  function h(tag, attrs, html) {
    var e = document.createElement(tag);
    for (var k in attrs || {}) if (attrs[k] != null && attrs[k] !== false) e.setAttribute(k, attrs[k] === true ? "" : attrs[k]);
    if (html != null) e.innerHTML = html;
    return e;
  }
  function tag(t) {
    if (typeof t === "string") t = { text: t };
    return '<span class="dk-tag dk-t-' + esc(t.tone || "neutral") + '">' + esc(t.text) + "</span>";
  }
  function highlight(code, lang) {
    var x = String(code).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    if (lang === "cs" || lang === "") x = x.replace(/\b(public|protected|private|internal|override|virtual|abstract|sealed|static|readonly|void|class|interface|record|namespace|using|new|return|var|async|await)\b/g, '<span class="k">$1</span>');
    if (lang === "xml") x = x.replace(/"([^"\n]*)"/g, '<span class="s">"$1"</span>');
    if (lang === "json") x = x.replace(/("[^"\n]*")/g, '<span class="s">$1</span>');
    x = x.replace(/(^|\n)(\s*(#|\/\/)[^\n]*)/g, '$1<span class="m">$2</span>');
    x = x.replace(/([^:"])(\/\/[^\n]*)/g, '$1<span class="m">$2</span>');
    return x.replace(/(&lt;!--[\s\S]*?--&gt;)/g, '<span class="m">$1</span>');
  }

  // Wide code becomes smaller until it fits (not below 7px).
  function fit(root) {
    (root || document).querySelectorAll(".dk-fit").forEach(function (el) {
      if (!el.offsetParent) return;
      el.style.fontSize = "";
      var cs = getComputedStyle(el), pad = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight), size = parseFloat(cs.fontSize);
      for (var i = 0; i < 6 && el.scrollWidth > el.clientWidth + 1 && size > 7; i++) {
        size = Math.max(7, size * (el.clientWidth - pad) / (el.scrollWidth - pad) - 0.1);
        el.style.fontSize = size.toFixed(2) + "px";
      }
    });
  }

  // ---- content blocks (inside folds) --------------------------------------
  var nested = 0, pending = [];
  function drawPending(root) {
    pending.splice(0).forEach(function (p) { var el = (root || document).querySelector("#" + p[0]) || document.getElementById(p[0]); if (el) render(el, p[1], p[2]); });
  }
  function block(b) {
    switch (b.type) {
      case "h": return "<h3>" + md(b.text) + "</h3>";
      case "callout": return '<div class="dk-callout">' + (b.icon ? '<span class="dk-ci">' + esc(b.icon) + "</span>" : "") + "<p>" + md(b.text) + "</p></div>";
      case "p": return '<p' + (b.muted ? ' class="dk-muted"' : "") + ">" + md(b.text) + "</p>";
      case "code": return '<figure class="dk-code">' + (b.cap ? "<figcaption>" + esc(b.cap) + "</figcaption>" : "") +
        '<pre class="dk-fit">' + highlight(b.code, b.lang || "") + "</pre></figure>";
      case "list": var t = b.ordered ? "ol" : "ul";
        return "<" + t + ' class="dk-list">' + b.items.map(function (i) { return "<li>" + md(i) + "</li>"; }).join("") + "</" + t + ">";
      case "kv": return '<dl class="dk-kv">' + b.items.map(function (i) { return "<dt>" + esc(i.label) + "</dt><dd>" + md(i.value) + "</dd>"; }).join("") + "</dl>";
      case "facts": return '<div class="dk-facts">' + b.items.map(function (i) {
        return '<div class="dk-fact dk-t-' + esc(i.tone || "neutral") + '"><b>' + esc(i.label) + "</b><span>" + esc(i.value) + "</span></div>"; }).join("") + "</div>";
      case "table": return '<div class="dk-table"><table>' + (b.head && b.head.length ? "<thead><tr>" + b.head.map(function (c) { return "<th>" + md(c) + "</th>"; }).join("") + "</tr></thead>" : "") +
        "<tbody>" + b.rows.map(function (r) { return "<tr>" + r.map(function (c) { return "<td>" + md(c) + "</td>"; }).join("") + "</tr>"; }).join("") + "</tbody></table></div>";
      case "cols": return '<div class="dk-cols">' + b.items.map(function (c) {
        return '<div class="dk-col dk-t-' + esc(c.tone || "neutral") + '"><b>' + esc(c.title) + '</b><ul class="dk-list">' + c.items.map(function (i) { return "<li>" + md(i) + "</li>"; }).join("") + "</ul></div>"; }).join("") + "</div>";
      default:
        if (TYPES[b.type] && b.data) { var id = "dk-n" + (++nested); pending.push([id, b.type, b.data]); return '<div id="' + id + '"></div>'; }
        return "<p>" + md(b.text || "") + "</p>";
    }
  }

  // ---- 1. tree ------------------------------------------------------------
  function tree(el, d) {
    var tones = d.tags || {}, rows = d.rows, open = {}, hideLater = false, more = {};
    function parent(p) { var i = p.lastIndexOf("/"); return i < 0 ? "" : p.slice(0, i); }
    function base(p) { var i = p.lastIndexOf("/"); return i < 0 ? p : p.slice(i + 1); }
    function isFile(p) { return /\.[a-z]+$/i.test(base(p)); }
    function kids(p) { return rows.filter(function (r) { return parent(r.path) === p && !(hideLater && r.when === "later"); }); }
    rows.forEach(function (r) { if (r.path.indexOf("/") < 0) open[r.path] = true; });
    var bar = h("div", { class: "dk-bar" }), box = h("div", { class: "dk-panel dk-tree", role: "tree" });
    var bOpen = h("button", { type: "button" }, "Open all"), bClose = h("button", { type: "button" }, "Close all");
    var bLater = h("button", { type: "button", "aria-pressed": "false" }, "Hide later"), count = h("span", { class: "dk-count" });
    [bOpen, bClose, bLater, count].forEach(function (x) { bar.appendChild(x); });
    function line(r, depth, out) {
      var ch = kids(r.path), isOpen = !!open[r.path], hasMore = r.entry || r.install;
      var row = h("div", { class: "dk-tr" + (r.when === "later" ? " dk-later" : "") + (hasMore ? " dk-has-more" : ""), role: "treeitem", style: "--d:" + depth });
      var tog = h("button", { type: "button", class: "dk-tog", "aria-expanded": String(isOpen), "aria-label": "Open or close " + r.path, hidden: !ch.length }, "▶");
      var name = '<span class="dk-name ' + (isFile(r.path) ? "dk-file" : "dk-dir") + '">' + esc(base(r.path)) + "</span>";
      var tags = (r.kind ? tag({ text: r.kind, tone: tones.kind }) : "") + (r.when === "later" ? tag({ text: "later", tone: tones.later }) : "");
      row.appendChild(tog);
      row.appendChild(h("span", { class: "dk-tn" }, name + tags));
      row.appendChild(h("span", { class: "dk-td", title: r.desc || "" }, md(r.desc || "")));
      tog.addEventListener("click", function () { open[r.path] = !open[r.path]; draw(); });
      if (hasMore) row.querySelector(".dk-td").addEventListener("click", function () { more[r.path] = !more[r.path]; draw(); });
      out.appendChild(row);
      if (more[r.path]) {
        out.appendChild(h("div", { class: "dk-tmore", style: "--d:" + depth },
          "<p>" + md(r.desc || "") + "</p>" + '<dl class="dk-kv">' + (r.kind ? "<dt>Kind</dt><dd>" + esc(r.kind) + "</dd>" : "") +
          (r.entry ? "<dt>Check entry</dt><dd>" + md(r.entry) + "</dd>" : "") + (r.install ? "<dt>Install point</dt><dd>" + md(r.install) + "</dd>" : "") + "</dl>"));
      }
      if (isOpen) ch.forEach(function (c) { line(c, depth + 1, out); });
    }
    function draw() {
      box.textContent = "";
      kids("").forEach(function (r) { line(r, 0, box); });
      count.textContent = rows.length + " lines";
    }
    bOpen.addEventListener("click", function () { rows.forEach(function (r) { open[r.path] = true; }); draw(); });
    bClose.addEventListener("click", function () { open = {}; draw(); });
    bLater.addEventListener("click", function () { hideLater = !hideLater; bLater.setAttribute("aria-pressed", String(hideLater)); draw(); });
    el.appendChild(bar); el.appendChild(box); draw();
  }

  // ---- 2. columns ---------------------------------------------------------
  function columns(el, d) {
    var color = {};
    d.groups.forEach(function (g, i) { color[g.id] = "var(--dk-c" + (i % 6 + 1) + ")"; });
    var html = '<div class="dk-groups">' + d.groups.map(function (g) {
      return '<section class="dk-group" style="--g:' + color[g.id] + '"><header><b>' + esc(g.title) + "</b><span>" + md(g.desc) + "</span></header>" +
        g.items.map(function (it) {
          return '<div class="dk-box"><div class="dk-box-top"><span class="dk-name">' + esc(it.name) + "</span>" + (it.ref ? tag({ text: it.ref, tone: "accent" }) : "") +
            (it.flags || []).map(function (f) { return '<span class="dk-flag" title="Flag ' + esc(f) + '">' + esc(f) + "</span>"; }).join("") +
            (it.meta ? '<span class="dk-meta">' + esc(it.meta) + "</span>" : "") + '</div><span class="dk-desc">' + md(it.desc) + "</span></div>";
        }).join("") + "</section>";
    }).join("") + "</div>";
    if (d.bar) html += "<h3>" + esc(d.bar.title) + '</h3><div class="dk-sizes" role="img" aria-label="' + esc(d.bar.parts.map(function (p) { return p.label; }).join(", ")) + '">' +
      d.bar.parts.map(function (p) { return '<div style="flex:' + p.value + ";--g:" + color[p.group] + '" title="' + esc(p.label) + '">' + esc(p.label) + "</div>"; }).join("") + "</div>";
    if (d.flags) html += "<h3>" + esc(d.flags.title) + '</h3><div class="dk-table"><table><thead><tr><th>#</th><th>From</th><th>To</th><th>Problem</th></tr></thead><tbody>' +
      d.flags.rows.map(function (r) {
        return '<tr><td><span class="dk-flag">' + esc(r.flag) + '</span></td><td class="dk-gtext" style="--g:' + color[r.from.group] + '">' + md(r.from.text) +
          '</td><td class="dk-gtext" style="--g:' + color[r.to.group] + '">' + md(r.to.text) + "</td><td>" + md(r.problem) + "</td></tr>";
      }).join("") + "</tbody></table></div>";
    el.insertAdjacentHTML("beforeend", html);
  }

  // ---- 3. graph -----------------------------------------------------------
  function graph(el, d) {
    var W = 760, NW = 172, NH = 56, ROWH = 112, top = 16, nodes = {};
    d.nodes.forEach(function (n) {
      var cols = d.rows[n.row] || 3, cw = W / cols, w = Math.min(NW + (cols < 4 ? 28 : 0), cw - 24);
      nodes[n.id] = { n: n, x: cw * (n.col + 0.5), y: top + n.row * ROWH + NH / 2, w: w, h: NH };
    });
    var H = top + d.rows.length * ROWH - (ROWH - NH) + 16;
    function edgePoint(a, b) { // where the line from a's center to b's center leaves a's box
      var dx = b.x - a.x, dy = b.y - a.y, sx = Math.abs(dx) > 0 ? (a.w / 2) / Math.abs(dx) : Infinity, sy = Math.abs(dy) > 0 ? (a.h / 2) / Math.abs(dy) : Infinity, s = Math.min(sx, sy);
      return { x: a.x + dx * s, y: a.y + dy * s };
    }
    var svg = '<svg viewBox="0 0 ' + W + " " + H + '" role="img" aria-label="' + esc(d.title || "Graph") + '"><defs><marker id="dk-ar" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path class="arrow" d="M0,0 L10,5 L0,10 z"/></marker></defs>';
    d.edges.forEach(function (e) {
      var a = nodes[e.from], b = nodes[e.to]; if (!a || !b) return;
      var p = edgePoint(a, b), q = edgePoint(b, a), len = Math.hypot(q.x - p.x, q.y - p.y), k = (len - 3) / len;
      var qx = p.x + (q.x - p.x) * k, qy = p.y + (q.y - p.y) * k;
      svg += '<g class="e' + (e.dashed ? " dashed" : "") + '"><line x1="' + p.x.toFixed(1) + '" y1="' + p.y.toFixed(1) + '" x2="' + qx.toFixed(1) + '" y2="' + qy.toFixed(1) + '" marker-end="url(#dk-ar)"/>';
      if (e.label) {
        var mx = (p.x + q.x) / 2, my = (p.y + q.y) / 2, vertical = Math.abs(q.x - p.x) < 4;
        svg += '<text x="' + (mx + (vertical ? 8 : 0)).toFixed(1) + '" y="' + (my + (vertical ? 4 : -6)).toFixed(1) + '" text-anchor="' + (vertical ? "start" : "middle") + '">' + esc(e.label) + "</text>";
      }
      svg += "</g>";
    });
    Object.keys(nodes).forEach(function (id) {
      var o = nodes[id], n = o.n;
      svg += '<g class="n ' + esc(n.kind || "plain") + '"><rect x="' + (o.x - o.w / 2).toFixed(1) + '" y="' + (o.y - o.h / 2) + '" width="' + o.w.toFixed(1) + '" height="' + o.h + '" rx="6"/>' +
        '<text class="t" x="' + o.x.toFixed(1) + '" y="' + (o.y - (n.sub ? 3 : -5)) + '">' + esc(n.label) + "</text>" +
        (n.sub ? '<text class="s" x="' + o.x.toFixed(1) + '" y="' + (o.y + 15) + '">' + esc(n.sub) + "</text>" : "") + "</g>";
    });
    svg += "</svg>";
    var html = (d.summary ? "<p>" + md(d.summary) + "</p>" : "") + '<div class="dk-panel dk-graph">' + svg + "</div>";
    if (d.legend) html += '<div class="dk-legend">' + d.legend.map(function (l) { return '<span><i class="' + esc(l.kind) + '"></i>' + esc(l.text) + "</span>"; }).join("") + "</div>";
    if (d.note) html += '<p class="dk-muted">' + md(d.note) + "</p>";
    el.insertAdjacentHTML("beforeend", html);
  }

  // ---- 4. stack -----------------------------------------------------------
  function stackRows(nodes, prefix, out, isRoot) {
    nodes.forEach(function (n, i) {
      var last = i === nodes.length - 1, glyph = isRoot ? "" : (last ? "└─ " : "├─ ");
      var notes = (n.note || "").split("\n"), more = isRoot ? "" : (last ? "   " : "│  ");
      out.push('<div class="dk-sl"><span class="dk-sc"><span class="g">' + esc(prefix + glyph) + "</span>" +
        (n.key ? '<span class="key">' + esc(n.call) + "</span>" : esc(n.call)) + '</span><span class="dk-sn">' + md(notes[0]) + "</span></div>");
      notes.slice(1).forEach(function (line) { // a note with more lines: the tree lines go on
        out.push('<div class="dk-sl"><span class="dk-sc"><span class="g">' + esc(prefix + more + (n.children ? "│" : "")) + '</span></span><span class="dk-sn">' + md(line) + "</span></div>");
      });
      if (n.children) stackRows(n.children, isRoot ? prefix : prefix + (last ? "   " : "│  "), out, false);
    });
  }
  function stack(el, d) {
    el.insertAdjacentHTML("beforeend", d.groups.map(function (g) {
      return "<h3>" + esc(g.title) + "</h3>" + (g.summary ? '<p class="dk-muted">' + md(g.summary) + "</p>" : "") +
        '<div class="dk-stacks">' + g.stacks.map(function (s) {
          var out = []; stackRows(s.root, "", out, true);
          return '<div class="dk-stack dk-fit"><div class="dk-stack-rows">' + out.join("") + "</div>" + (s.foot ? '<div class="dk-sfoot">' + md(s.foot) + "</div>" : "") + "</div>";
        }).join("") + "</div>";
    }).join(""));
  }

  // ---- 5 and 6. folds (children make a composed fold) --------------------
  function fold(f) {
    var tags = (f.tags || []).map(tag).join("");
    return '<details class="dk-fold"' + (f.open ? " open" : "") + '><summary><span class="dk-ft' + (f.mono ? " dk-mono" : "") + '">' + esc(f.title) + "</span>" +
      (tags ? '<span class="dk-ftags">' + tags + "</span>" : "") + (f.summary ? '<span class="dk-fs">' + md(f.summary) + "</span>" : "") + "</summary>" +
      '<div class="dk-fold-body">' + (f.body || []).map(block).join("") + (f.children ? '<div class="dk-folds">' + f.children.map(fold).join("") + "</div>" : "") + "</div></details>";
  }
  function folds(el, d) {
    var bar = h("div", { class: "dk-bar" }), list = h("div", { class: "dk-folds" }, d.items.map(fold).join(""));
    var bOpen = h("button", { type: "button" }, "Open all"), bClose = h("button", { type: "button" }, "Close all");
    bOpen.addEventListener("click", function () { list.querySelectorAll("details").forEach(function (x) { x.open = true; }); fit(list); });
    bClose.addEventListener("click", function () { list.querySelectorAll("details").forEach(function (x) { x.open = false; }); });
    bar.appendChild(bOpen); bar.appendChild(bClose);
    if (d.title) el.appendChild(h("h3", null, esc(d.title)));
    el.appendChild(bar); el.appendChild(list);
  }

  var TYPES = { tree: tree, columns: columns, graph: graph, stack: stack, folds: folds };
  function render(el, type, data) {
    el.classList.add("dk");
    el.textContent = "";
    TYPES[type](el, data);
    drawPending(el);
    fit(el);
  }

  document.addEventListener("toggle", function (e) { if (e.target.open) fit(e.target); }, true);
  var t; window.addEventListener("resize", function () { clearTimeout(t); t = setTimeout(function () { fit(); }, 120); });
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(function () { fit(); });

  function auto() {
    document.querySelectorAll("[data-dk][data-src]").forEach(function (el) {
      fetch(el.getAttribute("data-src")).then(function (r) { if (!r.ok) throw new Error(r.status + " " + r.statusText); return r.json(); })
        .then(function (d) { render(el, el.getAttribute("data-dk"), d); })
        .catch(function (e) { el.innerHTML = '<p class="dk-muted">This block did not load: ' + esc(e.message) + "</p>"; });
    });
  }
  window.DocKit = { render: render, fit: fit, block: block, types: TYPES, drawPending: drawPending };
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", auto); else auto();
})();
