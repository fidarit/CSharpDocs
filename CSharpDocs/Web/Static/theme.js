// Тема до отрисовки — чтобы не мигало. Значения: system | light | dark.
(function () {
  var t = "system";
  try { t = localStorage.getItem("csharp-docs:theme") || "system"; } catch (e) {}
  document.documentElement.dataset.theme = t;
})();
