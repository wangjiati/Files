/*
 * Files 内部分支 · 功能差异说明
 * 无依赖：主题切换、章节高亮、代码复制。
 */
(function () {
  'use strict';

  var root = document.documentElement;
  var toggle = document.getElementById('themeToggle');
  var label = document.getElementById('themeLabel');
  var media = window.matchMedia('(prefers-color-scheme: dark)');
  var modes = ['auto', 'light', 'dark'];
  var labels = { auto: '跟随系统', light: '浅色', dark: '深色' };
  var storageKey = 'files-fork-theme';

  function currentMode() {
    var mode = root.dataset.themeMode;
    return modes.indexOf(mode) >= 0 ? mode : 'auto';
  }

  function applyTheme(mode) {
    var resolved = mode === 'auto' ? (media.matches ? 'dark' : 'light') : mode;
    root.dataset.theme = resolved;
    root.dataset.themeMode = mode;
    if (label) {
      label.textContent = labels[mode];
    }
    if (toggle) {
      toggle.setAttribute('aria-label', '主题：' + labels[mode] + '（点击切换）');
      toggle.setAttribute('title', '主题：' + labels[mode] + '（点击切换）');
    }
  }

  applyTheme(currentMode());

  if (toggle) {
    toggle.addEventListener('click', function () {
      var next = modes[(modes.indexOf(currentMode()) + 1) % modes.length];
      applyTheme(next);
      try {
        localStorage.setItem(storageKey, next);
      } catch (e) {
        /* 隐私模式下无法持久化，忽略即可。 */
      }
    });
  }

  var onSystemChange = function () {
    if (currentMode() === 'auto') {
      applyTheme('auto');
    }
  };

  if (typeof media.addEventListener === 'function') {
    media.addEventListener('change', onSystemChange);
  } else if (typeof media.addListener === 'function') {
    media.addListener(onSystemChange);
  }

  // 章节高亮（左侧顶部导航 + 右侧目录）
  var navLinks = Array.prototype.slice.call(document.querySelectorAll('.site-nav a[href^="#"]'));
  var tocLinks = Array.prototype.slice.call(document.querySelectorAll('.toc a[href^="#"]'));
  var linksById = {};

  navLinks.concat(tocLinks).forEach(function (link) {
    var id = link.getAttribute('href').slice(1);
    if (!document.getElementById(id)) {
      return;
    }
    if (!linksById[id]) {
      linksById[id] = [];
    }
    linksById[id].push(link);
  });

  var sections = Object.keys(linksById)
    .map(function (id) { return document.getElementById(id); })
    .filter(Boolean)
    .sort(function (a, b) {
      return (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING) ? -1 : 1;
    });

  var lastActiveId = null;

  function updateActiveSection() {
    var currentId = null;

    for (var i = 0; i < sections.length; i++) {
      if (sections[i].getBoundingClientRect().top <= 132) {
        currentId = sections[i].id;
      }
    }

    if (!currentId && window.scrollY < 240) {
      currentId = 'overview';
    }

    if (currentId === lastActiveId) {
      return;
    }

    lastActiveId = currentId;

    Object.keys(linksById).forEach(function (id) {
      linksById[id].forEach(function (link) {
        if (id === currentId) {
          link.setAttribute('aria-current', 'true');
        } else {
          link.removeAttribute('aria-current');
        }
      });
    });
  }

  var spyTicking = false;

  function requestSpyUpdate() {
    if (spyTicking) {
      return;
    }

    spyTicking = true;
    window.requestAnimationFrame(function () {
      spyTicking = false;
      updateActiveSection();
    });
  }

  window.addEventListener('scroll', requestSpyUpdate, { passive: true });
  window.addEventListener('resize', requestSpyUpdate);
  updateActiveSection();

  // 代码复制
  function fallbackCopy(text) {
    var area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.top = '-1000px';
    document.body.appendChild(area);
    area.select();
    var ok = false;
    try {
      ok = document.execCommand('copy');
    } catch (e) {
      ok = false;
    }
    document.body.removeChild(area);
    return ok;
  }

  Array.prototype.forEach.call(document.querySelectorAll('.code-block__copy'), function (button) {
    button.addEventListener('click', function () {
      var block = button.closest('.code-block');
      var code = block ? block.querySelector('pre code') : null;
      if (!code) {
        return;
      }

      var text = code.textContent;
      var done = function () {
        var original = button.textContent;
        button.textContent = '已复制';
        window.setTimeout(function () {
          button.textContent = original;
        }, 1500);
      };

      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(done, function () {
          if (fallbackCopy(text)) {
            done();
          }
        });
      } else if (fallbackCopy(text)) {
        done();
      }
    });
  });
})();
