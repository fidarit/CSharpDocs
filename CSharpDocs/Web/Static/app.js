(function () {
  "use strict";

  // --- Тема: system → light → dark по кругу ---
  var THEME_STORE = "csharp-docs:theme";
  var THEME_ICONS = { system: "\u25D0", light: "\u2600", dark: "\u263D" };
  var THEME_TITLES = { system: "Тема: системная", light: "Тема: светлая", dark: "Тема: тёмная" };

  function initTheme() {
    var button = document.getElementById("theme-toggle");
    if (!button) return;
    var theme = document.documentElement.dataset.theme || "system";
    renderTheme(button, theme);
    button.addEventListener("click", function () {
      var next = theme === "system" ? "light" : theme === "light" ? "dark" : "system";
      theme = next;
      document.documentElement.dataset.theme = next;
      try { localStorage.setItem(THEME_STORE, next); } catch (e) {}
      renderTheme(button, next);
    });
  }

  function renderTheme(button, theme) {
    button.textContent = THEME_ICONS[theme] || THEME_ICONS.system;
    button.title = THEME_TITLES[theme] || THEME_TITLES.system;
  }

  var HISTORY_STORE = "csharp-docs:history";
  var MAX_HISTORY = 3;
  var treeEl = document.getElementById("tree");
  var contentEl = document.getElementById("content");
  var searchEl = document.getElementById("q");
  var clearEl = document.getElementById("q-clear");

  var entries = [];              // все узлы дерева (для фильтрации)
  var byKey = {};                // key -> entry
  var bySlug = {};               // slug типа -> entry (для подсветки/скролла)
  var visited = loadHistory();   // последние посещённые страницы (ключи узлов), не более MAX_HISTORY
  var searching = false;

  // --- История посещений (3 последних страницы) ---
  function loadHistory() {
    try { var a = JSON.parse(localStorage.getItem(HISTORY_STORE)); return Array.isArray(a) ? a : []; }
    catch (e) { return []; }
  }
  function saveHistory() {
    try { localStorage.setItem(HISTORY_STORE, JSON.stringify(visited)); } catch (e) {}
  }
  function rememberVisit(key) {
    if (!key || visited[0] === key) return;
    visited = [key].concat(visited.filter(function (k) { return k !== key; })).slice(0, MAX_HISTORY);
    saveHistory();
  }

  // --- Построение дерева ---
  function buildForest(list, parentKey) {
    var ul = document.createElement("ul");
    list.forEach(function (data) { ul.appendChild(buildNode(data, parentKey)); });
    return ul;
  }

  function buildNode(data, parentKey) {
    var li = document.createElement("li");
    var children = data.children || [];
    var hasChildren = children.length > 0;

    var node = document.createElement("div");
    node.className = "node";
    node.dataset.key = data.key;
    li.appendChild(node);

    var toggle = document.createElement("span");
    toggle.className = "toggle" + (hasChildren ? "" : " empty");
    toggle.textContent = "\u25b8";
    node.appendChild(toggle);

    var kind = document.createElement("span");
    kind.className = "kind kind-" + slugClass(data.kind);
    kind.textContent = data.kind;
    node.appendChild(kind);

    if (data.slug) {
      var a = document.createElement("a");
      a.href = "/type/" + data.slug;
      a.textContent = data.name;
      node.appendChild(a);
    } else {
      var span = document.createElement("span");
      span.className = "nsname";
      span.textContent = data.name;
      node.appendChild(span);
    }

    if (data.ext > 0) {
      var ext = document.createElement("span");
      ext.className = "ext";
      ext.title = "методов-расширений: " + data.ext;
      ext.textContent = "+" + data.ext;
      node.appendChild(ext);
    }

    var entry = {
      li: li, node: node, box: null, parentKey: parentKey,
      slug: data.slug || "",
      short: data.name.toLowerCase(),
      full: (data.full || data.name).toLowerCase(),
      // описание/примечания/сигнатура — для поиска по смыслу, не только по имени
      search: (data.search || "").toLowerCase(),
      index: entries.length,   // исходный порядок (для сброса)
      rank: 0,                 // 2 — совпало имя, 1 — только описание, 0 — нет
      comments: 0,             // собственные комментарии
      total: 0,                // с учётом потомков
      badge: null
    };
    entries.push(entry);
    byKey[data.key] = entry;
    if (data.slug) bySlug[data.slug] = entry;

    if (hasChildren) {
      var box = document.createElement("div");
      box.className = "children";
      box.appendChild(buildForest(children, data.key));
      li.appendChild(box);
      entry.box = box;
      setBoxOpen(node, false);   // раскрытие задаётся recomputeExpansion()

      // Ручное раскрытие/скрытие — временное, не сохраняется (иначе снова «куча раскрытых»).
      toggle.addEventListener("click", function (e) {
        e.stopPropagation();
        setBoxOpen(node, !box.classList.contains("open"));
      });
    }
    return li;
  }

  function slugClass(kind) { return kind.replace(/[^a-zа-яё0-9]/gi, "-"); }

  function setBoxOpen(node, expanded) {
    var box = node.parentElement.querySelector(":scope > .children");
    if (box) box.classList.toggle("open", expanded);
    var t = node.querySelector(":scope > .toggle");
    if (t && !t.classList.contains("empty")) t.textContent = expanded ? "\u25be" : "\u25b8";
  }

  // Подсвечивает узел текущей страницы. Возвращает entry или null.
  function setActiveClass(slug) {
    treeEl.querySelectorAll(".node.active").forEach(function (n) { n.classList.remove("active"); });
    var entry = slug ? bySlug[slug] : null;
    if (entry) entry.node.classList.add("active");
    return entry;
  }

  // Активная страница: раскрываем предков, но саму ноду не раскрываем
  // (её содержимое — это уже и есть текущая страница).
  function markActive(slug, scroll) {
    var entry = setActiveClass(slug);
    if (!entry) return;
    rememberVisit(entry.node.dataset.key);
    // Раскрываем только пути к последним посещённым страницам — без накопления.
    recomputeExpansion();
    if (scroll !== false) scrollTreeTo(entry.node);
  }

  // Раскрывает только пути к последним посещённым страницам; всё прочее свернуто.
  function recomputeExpansion() {
    entries.forEach(function (e) { if (e.box) setBoxOpen(e.node, false); });
    visited.forEach(revealPath);
  }

  // Раскрывает предков узла key (сам узел не раскрываем).
  function revealPath(key) {
    var e = byKey[key];
    for (var pk = e ? e.parentKey : null; pk; pk = byKey[pk] ? byKey[pk].parentKey : null) {
      var p = byKey[pk];
      if (p && p.box) setBoxOpen(p.node, true);
    }
  }

  function scrollTreeTo(node) {
    var nodeRect = node.getBoundingClientRect();
    var treeRect = treeEl.getBoundingClientRect();
    if (nodeRect.top < treeRect.top || nodeRect.bottom > treeRect.bottom)
      treeEl.scrollTop += nodeRect.top - treeRect.top - treeEl.clientHeight / 3;
  }

  // --- Индикация комментариев в дереве ---
  // counts: { slug: число }. Бейдж показываем у узла и у всех его предков
  // (итог по поддереву) — как счётчик комментариев у папки.
  function applyCommentBadges(counts) {
    for (var i = 0; i < entries.length; i++) {
      var e = entries[i];
      e.comments = e.slug && counts[e.slug] ? counts[e.slug] : 0;
      e.total = 0;
    }
    for (var j = 0; j < entries.length; j++) {
      var o = entries[j];
      if (!o.comments) continue;
      for (var k = o.node.dataset.key; k; k = byKey[k] ? byKey[k].parentKey : null)
        byKey[k].total += o.comments;
    }
    for (var m = 0; m < entries.length; m++)
      renderBadge(entries[m]);
  }

  function renderBadge(e) {
    if (e.total <= 0) {
      if (e.badge) { e.badge.remove(); e.badge = null; }
      return;
    }
    if (!e.badge) {
      e.badge = document.createElement("span");
      e.badge.className = "comment-badge";
      e.node.appendChild(e.badge);
    }
    e.badge.textContent = e.total;
    e.badge.title = "Комментариев (с учётом вложенных): " + e.total;
  }

  // --- Клиентский поиск по всему дереву (с учётом вложенности) ---
  var SEARCH_DEBOUNCE_MS = 200;
  var searchTimer = null;
  var searchToken = 0;   // отменяет устаревшие проходы

  function onSearchInput() {
    var q = searchEl.value.trim();
    clearEl.hidden = q.length === 0;
    updateUrlQuery(q);

    // Немедленно сбрасываем при очистке, иначе применяем с задержкой.
    if (searchTimer) clearTimeout(searchTimer);
    searchToken++;
    if (q.length === 0) { resetFilter(); return; }
    searchTimer = setTimeout(function () { applyFilter(q, searchToken); }, SEARCH_DEBOUNCE_MS);
  }

  // Ранг совпадения: 2 — совпало имя, 1 — только текст описания, 0 — нет.
  function makeMatcher(q) {
    if (q.indexOf("*") < 0 && q.indexOf("?") < 0) {
      var lower = q.toLowerCase();
      return function (e) {
        if (e.short.indexOf(lower) >= 0 || e.full.indexOf(lower) >= 0) return 2;
        return e.search.indexOf(lower) >= 0 ? 1 : 0;
      };
    }
    var pattern = q.toLowerCase()
      .replace(/[.+^${}()|[\]\\]/g, "\\$&")
      .replace(/\*/g, ".*")
      .replace(/\?/g, ".");
    var re = new RegExp("^" + pattern + "$");
    return function (e) { return re.test(e.short) || re.test(e.full) ? 2 : 0; };
  }

  // Считает ранги и возвращает множество видимых узлов (совпадения + предки).
  function computeKeep(rankOf) {
    var keep = new Set();
    for (var i = 0; i < entries.length; i++) {
      var e = entries[i];
      e.rank = rankOf(e);
      if (e.rank === 0) continue;
      for (var key = e.node.dataset.key; key; key = byKey[key] ? byKey[key].parentKey : null)
        keep.add(key);
    }
    return keep;
  }

  // Применяет фильтр асинхронно, порциями — интерфейс не блокируется на 16k узлах.
  function applyFilter(q, token) {
    searching = true;
    var keep = computeKeep(makeMatcher(q));

    var CHUNK = 800;
    var i = 0;

    function step() {
      if (token !== searchToken) return;   // пришёл новый запрос
      var end = Math.min(i + CHUNK, entries.length);
      for (; i < end; i++) {
        var e = entries[i];
        e.li.hidden = !keep.has(e.node.dataset.key);
      }
      if (i < entries.length) { setTimeout(step, 0); return; }

      entries.forEach(function (e) { if (e.box) setBoxOpen(e.node, false); });
      var activePath = activePathKeys();
      activePath.forEach(function (key) {
        var e = byKey[key];
        if (e && e.box) setBoxOpen(e.node, true);
      });
      reorderByRank();
      // Текущая страница должна подсвечиваться и во время поиска.
      var active = setActiveClass(currentSlugFromPath());
      if (active && !active.li.closest("li[hidden]")) scrollTreeTo(active.node);
      toggleEmptyNotice(keep.size === 0);
    }
    step();
  }

  // Ключи предков текущей страницы (сам активный узел не включаем — его
  // раскрывать не нужно, это и есть открытая страница).
  function activePathKeys() {
    var keys = new Set();
    var slug = currentSlugFromPath();
    var start = slug ? bySlug[slug] : null;
    for (var pk = start ? start.parentKey : null; pk; pk = byKey[pk] ? byKey[pk].parentKey : null)
      keys.add(pk);
    return keys;
  }

  // Переупорядочивает соседей внутри каждого списка заданным компаратором.
  function sortSiblings(compare) {
    var lists = new Set();
    for (var i = 0; i < entries.length; i++) {
      var li = entries[i].li;
      if (li.parentNode) lists.add(li.parentNode);
    }
    lists.forEach(function (ul) {
      var items = Array.prototype.slice.call(ul.children);
      items.sort(function (a, b) {
        return compare(byKey[a.firstChild.dataset.key], byKey[b.firstChild.dataset.key]);
      });
      items.forEach(function (li) { ul.appendChild(li); });
    });
  }

  // Совпадения по имени — выше, затем по описанию; внутри ранга — исходный порядок.
  function reorderByRank() {
    sortSiblings(function (ea, eb) {
      return eb.rank !== ea.rank ? eb.rank - ea.rank : ea.index - eb.index;
    });
  }

  function toggleEmptyNotice(show) {
    var notice = document.getElementById("search-empty");
    if (!show) { if (notice) notice.remove(); return; }
    if (notice) return;
    notice = document.createElement("p");
    notice.id = "search-empty";
    notice.className = "muted";
    notice.textContent = "Ничего не найдено.";
    treeEl.parentElement.appendChild(notice);
  }

  function resetFilter() {
    toggleEmptyNotice(false);
    if (!searching) return;
    searching = false;
    entries.forEach(function (e) {
      e.li.hidden = false;
      e.rank = 0;
    });
    restoreOrder();
    recomputeExpansion();
    markActive(currentSlugFromPath());
  }

  // Возвращает исходный порядок соседей (по сохранённому index).
  function restoreOrder() {
    sortSiblings(function (ea, eb) { return ea.index - eb.index; });
  }

  // --- Запрос в URL ---
  function updateUrlQuery(q) {
    var url = new URL(location.href);
    if (q) url.searchParams.set("q", q); else url.searchParams.delete("q");
    history.replaceState(history.state, "", url.pathname + url.search);
  }

  function applyQueryFromUrl() {
    var q = new URL(location.href).searchParams.get("q") || "";
    searchEl.value = q;
    clearEl.hidden = q.length === 0;
    if (searchTimer) clearTimeout(searchTimer);
    searchToken++;
    if (q.length === 0) resetFilter(); else applyFilter(q, searchToken);
  }

  // --- AJAX-навигация ---
  // Полный слаг выбранного элемента: тип («…/ipart») или член («…/ipart/…-marking-…»).
  function currentSlugFromPath() {
    var m = location.pathname.match(/^\/type\/(.+)$/);
    return m ? decodeURIComponent(m[1]) : null;
  }

  function loadUrl(url, push) {
    fetch(url, { headers: { "X-Requested-With": "fetch" } })
      .then(function (r) {
        if (!r.ok) { location.href = url; return null; }
        return r.text();
      })
      .then(function (html) {
        if (html === null) return;
        contentEl.innerHTML = html;
        if (push) history.pushState({ url: url }, "", url);
        afterNavigate();
      });
  }

  function afterNavigate() {
    bindCommentForms();
    var slug = currentSlugFromPath();
    // Во время поиска не трогаем раскрытие/скролл, но подсветку текущей — держим.
    if (searching) setActiveClass(slug); else markActive(slug);
    contentEl.scrollTop = 0;
    window.scrollTo(0, 0);
  }

  function isInternalLink(a) {
    if (!a || !a.getAttribute("href")) return false;
    var href = a.getAttribute("href");
    if (href.charAt(0) === "#" || a.target || a.hasAttribute("download")) return false;
    return href.indexOf("http://") !== 0 && href.indexOf("https://") !== 0 && href.indexOf("//") !== 0;
  }

  document.addEventListener("click", function (e) {
    var a = e.target.closest && e.target.closest("a");
    if (!isInternalLink(a)) return;
    if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button !== 0) return;
    e.preventDefault();
    loadUrl(a.getAttribute("href"), true);
  });

  window.addEventListener("popstate", function () {
    loadUrl(location.pathname + location.search, false);
    if (!searching) applyQueryFromUrl();
  });

  // --- Комментарии ---
  function bindCommentForms() {
    var name = localStorage.getItem("csharp-docs:author") || "";
    contentEl.querySelectorAll(".comment-form").forEach(function (f) {
      var input = f.querySelector('input[name="author"]');
      if (input && !input.value) input.value = name;
      f.addEventListener("submit", function () {
        if (input) { try { localStorage.setItem("csharp-docs:author", input.value); } catch (e) {} }
        // После сохранения комментария обновим счётчики в дереве.
        setTimeout(loadCommentCounts, 300);
      });
    });
  }

  document.addEventListener("keydown", function (e) {
    if (e.key === "/" && document.activeElement.tagName !== "INPUT" && document.activeElement.tagName !== "TEXTAREA") {
      e.preventDefault(); searchEl.focus(); searchEl.select();
    } else if (e.key === "Escape" && document.activeElement === searchEl) {
      searchEl.value = ""; onSearchInput();
    }
  });

  searchEl.addEventListener("input", onSearchInput);
  clearEl.addEventListener("click", function () { searchEl.value = ""; onSearchInput(); searchEl.focus(); });

  // --- Инициализация ---
  fetch("/api/tree")
    .then(function (r) { return r.json(); })
    .then(function (data) {
      treeEl.appendChild(buildForest(data, null));
      recomputeExpansion();
      applyQueryFromUrl();
      if (!searching) markActive(currentSlugFromPath());
      loadCommentCounts();
    })
    .catch(function () {});

  bindCommentForms();
  initTheme();

  function loadCommentCounts() {
    fetch("/api/comments")
      .then(function (r) { return r.json(); })
      .then(applyCommentBadges)
      .catch(function () {});
  }
})();
