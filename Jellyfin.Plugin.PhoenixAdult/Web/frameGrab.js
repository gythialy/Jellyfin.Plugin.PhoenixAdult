/*
 * PhoenixAdult 抽帧入口：注入到 Jellyfin web 客户端。
 *
 * Jellyfin 10.11 起 web 客户端是 React 重写版（#reactRoot + hash 路由 #/details?id=…），
 * 旧的 .actionSheet / .btnMoreCommands / #itemDetailPage 等类名已不存在，无法稳定地往原生「⋯」菜单插项，
 * 因此提供两个入口：
 *   1) 原生「⋯」菜单（MUI Menu）末尾追加一项
 *   2) 详情页右下角浮动按钮（菜单注入失效时的兜底）
 *
 * 外观全部走主题 CSS 变量（SleekFin 的 --sleekfin-* 优先，回退 Jellyfin 的 --jf-*），
 * 这样与主题按钮完全一致，且切换主题时自动跟随（不复制计算样式）。
 *
 * 抓帧与上传都在浏览器侧完成：canvas 抓帧 → base64 → POST /Items/{id}/Images/{Primary|Backdrop}。
 * 鉴权：Jellyfin 12.4 只接受 Authorization: MediaBrowser Token="…"。
 */
(function () {
    'use strict';

    var FAB_ID = 'phoenixAdultFrameGrabFab';
    var MENU_CLASS = 'phoenixAdultFrameGrabMenuItem';
    var STYLE_ID = 'phoenixAdultFrameGrabStyle';
    var availability = {};
    var checking = {};

    /* ---------------- i18n ---------------- */

    var I18N = {
        zh: {
            fab: '抽帧',
            menu: '抽帧（PhoenixAdult）',
            title: '抽帧（PhoenixAdult）',
            primary: '设为主图',
            backdrop: '设为背景图',
            close: '关闭',
            uploading: '上传中…',
            savedPrimary: '已保存为主图。',
            savedBackdrop: '已保存为背景图。',
            saveFailed: '保存失败：',
            noFrame: '还没有可用的画面，先把视频播放/拖到要用的那一帧。',
            cannotPreview: '浏览器无法预览这个文件（常见于 HEVC / mkv），请改用本地 fanart.jpg / poster.jpg。'
        },
        en: {
            fab: 'Frame grab',
            menu: 'Frame grab (PhoenixAdult)',
            title: 'Frame grab (PhoenixAdult)',
            primary: 'Set as primary',
            backdrop: 'Set as backdrop',
            close: 'Close',
            uploading: 'Uploading…',
            savedPrimary: 'Saved as primary image.',
            savedBackdrop: 'Saved as backdrop image.',
            saveFailed: 'Save failed: ',
            noFrame: 'No frame available yet — play or seek to the frame you want first.',
            cannotPreview: 'This file cannot be previewed in the browser (HEVC / mkv). Use a local fanart.jpg / poster.jpg instead.'
        }
    };

    function detectLanguage() {
        var candidates = [];
        try {
            candidates.push(document.documentElement.getAttribute('lang'));
            candidates.push(window.localStorage.getItem('language'));
            candidates.push(navigator.language);
        } catch (error) {
            /* 忽略 */
        }

        for (var i = 0; i < candidates.length; i++) {
            var value = (candidates[i] || '').toString().toLowerCase();
            if (value) {
                return value.indexOf('zh') === 0 ? 'zh' : 'en';
            }
        }

        return 'en';
    }

    var LANG = detectLanguage();

    function t(key) {
        return (I18N[LANG] || I18N.en)[key] || I18N.en[key] || key;
    }

    /* ---------------- 基础工具 ---------------- */

    function log(message) {
        if (window.console && console.log) {
            console.log('[PhoenixAdult] ' + message);
        }
    }

    function apiUrl(path, params) {
        return window.ApiClient.getUrl(path, params);
    }

    function authToken() {
        try {
            return window.ApiClient.accessToken ? window.ApiClient.accessToken() : null;
        } catch (error) {
            return null;
        }
    }

    function authHeaders(extra) {
        var headers = extra || {};
        var token = authToken();
        if (token) {
            headers.Authorization = 'MediaBrowser Token="' + token + '"';
        }
        return headers;
    }

    function apiGet(url) {
        var token = authToken();
        if (!token) {
            return window.ApiClient.getJSON
                ? Promise.resolve(window.ApiClient.getJSON(url))
                : Promise.reject(new Error('no access token'));
        }

        return fetch(url, { headers: authHeaders() }).then(function (response) {
            if (!response.ok) {
                throw new Error('HTTP ' + response.status);
            }
            return response.json();
        });
    }

    function apiPost(url, base64) {
        var token = authToken();
        if (!token && window.ApiClient.ajax) {
            return Promise.resolve(window.ApiClient.ajax({
                type: 'POST',
                url: url,
                data: base64,
                contentType: 'image/jpeg'
            })).then(function () {
                return { ok: true, status: 204 };
            });
        }

        return fetch(url, {
            method: 'POST',
            headers: authHeaders({ 'Content-Type': 'image/jpeg' }),
            body: base64
        });
    }

    function currentItemId() {
        var hash = window.location.hash || '';
        var match = /[?&]id=([0-9a-fA-F-]{32,36})/.exec(hash);
        return match ? match[1] : null;
    }

    function isAdmin() {
        try {
            var current = window.ApiClient.getCurrentUser ? window.ApiClient.getCurrentUser() : null;
            return Promise.resolve(current)
                .then(function (user) {
                    return !!(user && user.Policy && user.Policy.IsAdministrator);
                })
                .catch(function () {
                    return false;
                });
        } catch (error) {
            return Promise.resolve(false);
        }
    }

    function isEnabled(itemId) {
        return isAdmin().then(function (admin) {
            if (!admin) {
                return false;
            }

            if (Object.prototype.hasOwnProperty.call(availability, itemId)) {
                return availability[itemId];
            }

            return apiGet(apiUrl('PhoenixAdult/framegrab/enabled', { itemId: itemId }))
                .then(function (data) {
                    availability[itemId] = !!(data && (data.Enabled === true || data.enabled === true));
                    return availability[itemId];
                })
                .catch(function (error) {
                    log('enabled check failed for ' + itemId + ': ' + (error && error.message ? error.message : error));
                    availability[itemId] = false;
                    return false;
                });
        });
    }

    /* ---------------- 图标（复用 Jellyfin 自带 Material Icons 字体） ---------------- */

    var FALLBACK_PATHS = {
        photo_camera: 'M9 2 7.17 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2h-3.17L15 2H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z',
        chevron_left: 'M15.41 7.41 14 6l-6 6 6 6 1.41-1.41L10.83 12z',
        chevron_right: 'M10 6 8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z',
        fast_rewind: 'M11 18V6l-8.5 6 8.5 6zm.5-6 8.5 6V6l-8.5 6z',
        fast_forward: 'M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z',
        image: 'M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z',
        landscape: 'M14 6l-3.75 5 2.85 3.8-1.6 1.2C9.81 13.75 7 10 7 10l-6 8h22L14 6z',
        close: 'M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z'
    };

    var iconFontReady = false;

    function warmIconFont() {
        try {
            if (!document.fonts || !document.fonts.load) {
                return;
            }

            document.fonts.load('24px "Material Icons"').then(function () {
                iconFontReady = document.fonts.check('24px "Material Icons"');
            }).catch(function () {
                iconFontReady = false;
            });
        } catch (error) {
            iconFontReady = false;
        }
    }

    function icon(name, size) {
        var px = size || 18;
        var useFont = iconFontReady;
        try {
            if (!useFont && document.fonts && document.fonts.check) {
                useFont = document.fonts.check('24px "Material Icons"');
            }
        } catch (error) {
            useFont = false;
        }

        if (useFont) {
            return '<span class="material-icons" aria-hidden="true" style="font-size:' + px + 'px;line-height:1;flex:none">' + name + '</span>';
        }

        var path = FALLBACK_PATHS[name] || FALLBACK_PATHS.photo_camera;
        return '<svg viewBox="0 0 24 24" width="' + px + '" height="' + px + '" aria-hidden="true" focusable="false" style="display:block;flex:none">'
            + '<path fill="currentColor" d="' + path + '"/></svg>';
    }

    function cameraIcon(size) {
        return icon('photo_camera', size || 20);
    }

    // The theme variables live on the details container, so copy the resolved
    // values onto our own element instead of falling back to the default accent.
    var THEME_VARS = [
        '--sleekfin-details-primary-button-fill',
        '--sleekfin-details-primary-button-text-color',
        '--sleekfin-details-primary-button-hover-fill',
        '--sleekfin-details-secondary-button-fill',
        '--sleekfin-details-secondary-button-text-color',
        '--sleekfin-details-secondary-button-hover-fill',
        '--sleekfin-details-button-radius',
        '--sleekfin-details-button-height',
        '--sleekfin-details-button-blur',
        '--sleekfin-details-button-spacing',
        '--sleekfin-border',
        '--sleekfin-border-hover',
        '--sleekfin-font-family',
        '--sleekfin-glass-shadow',
        '--sleekfin-accent'
    ];

    function themeSampleElement() {
        return document.querySelector('.sleekfin-button-content')
            || document.querySelector('.detailButton-content')
            || document.querySelector('.MuiButton-contained')
            || document.querySelector('.MuiButton-root')
            || document.querySelector('button');
    }

    function inheritThemeVars(element) {
        var sample = themeSampleElement();
        if (!element || !sample) {
            return;
        }

        var style = getComputedStyle(sample);
        THEME_VARS.forEach(function (name) {
            var value = style.getPropertyValue(name);
            if (value && value.trim()) {
                element.style.setProperty(name, value.trim());
            }
        });
    }

    /* ---------------- 样式：全部走主题变量 ---------------- */

    function injectStyle() {
        if (document.getElementById(STYLE_ID)) {
            return;
        }

        var style = document.createElement('style');
        style.id = STYLE_ID;
        style.textContent = ''
            // Entry button: same variables as the theme details page.
            + '.paFabButton{position:fixed;right:24px;bottom:24px;z-index:2147483000;display:inline-flex;align-items:center;justify-content:center;gap:8px;'
            + 'height:var(--sleekfin-details-button-height,40px);padding:0 20px;'
            + 'border-radius:var(--sleekfin-details-button-radius,999px);'
            + 'background:var(--sleekfin-details-secondary-button-fill,var(--jf-palette-background-paper,#1a1c20));'
            + 'color:var(--sleekfin-details-secondary-button-text-color,var(--jf-palette-text-primary,#f1f2f4));'
            + 'border:1px solid var(--sleekfin-border,var(--jf-palette-divider,rgba(127,127,127,.4)));'
            + 'backdrop-filter:blur(var(--sleekfin-details-button-blur,0px));-webkit-backdrop-filter:blur(var(--sleekfin-details-button-blur,0px));'
            + 'font-family:var(--sleekfin-font-family,var(--jf-font-button,inherit));font-size:.95rem;font-weight:500;line-height:1;'
            + 'box-shadow:var(--sleekfin-glass-shadow,var(--jf-shadows-2,0 4px 14px rgba(0,0,0,.35)));cursor:pointer;'
            + 'transition:background .15s ease,color .15s ease,border-color .15s ease,transform .15s ease}'
            + '.paFabButton:hover{background:var(--sleekfin-details-secondary-button-hover-fill,var(--jf-palette-action-hover,rgba(127,127,127,.18)));'
            + 'border-color:var(--sleekfin-border-hover,var(--jf-palette-action-selected,rgba(127,127,127,.5)));transform:translateY(-1px)}'
            + '.paFabButton:active{transform:none}'
            // Dialog.
            + '.paFgDialog{border:0;padding:0;border-radius:14px;max-width:min(960px,94vw);width:100%;'
            + 'background:var(--jf-palette-background-paper,var(--sleekfin-surface,#161618));'
            + 'color:var(--jf-palette-text-primary,var(--sleekfin-text,#f1f2f4));'
            + 'font-family:var(--sleekfin-font-family,inherit);box-shadow:0 18px 50px rgba(0,0,0,.6)}'
            + '.paFgDialog::backdrop{background:rgba(0,0,0,.72)}'
            + '.paFgBody{padding:18px;display:flex;flex-direction:column;gap:12px}'
            + '.paFgTitle{font-size:1.05rem;font-weight:600;display:flex;align-items:center;gap:8px}'
            + '.paFgVideo{width:100%;max-height:56vh;background:#000;border-radius:10px}'
            + '.paFgBar{display:flex;align-items:center;gap:8px;flex-wrap:wrap;font-size:.85rem;opacity:.9}'
            // Dialog buttons use the theme variables too.
            + '.paFgBtn{display:inline-flex;align-items:center;justify-content:center;gap:6px;height:34px;padding:0 14px;'
            + 'border-radius:var(--sleekfin-details-button-radius,8px);'
            + 'background:var(--sleekfin-details-secondary-button-fill,var(--jf-palette-action-hover,rgba(127,127,127,.16)));'
            + 'color:var(--sleekfin-details-secondary-button-text-color,var(--jf-palette-text-primary,inherit));'
            + 'border:1px solid var(--sleekfin-border,var(--jf-palette-divider,rgba(127,127,127,.4)));'
            + 'font-family:inherit;font-size:.9rem;font-weight:500;line-height:1;cursor:pointer;'
            + 'transition:background .15s ease,border-color .15s ease}'
            + '.paFgBtn:hover{background:var(--sleekfin-details-secondary-button-hover-fill,var(--jf-palette-action-selected,rgba(127,127,127,.28)));'
            + 'border-color:var(--sleekfin-border-hover,var(--jf-palette-action-selected,rgba(127,127,127,.6)));'
            + 'filter:brightness(1.18)}'
            + '.paFgSavePrimary{background:var(--sleekfin-details-primary-button-fill,var(--jf-palette-primary-main,#00a4dc));'
            + 'color:var(--sleekfin-details-primary-button-text-color,var(--jf-palette-primary-contrastText,#fff));font-weight:600;'
            + 'border-color:var(--sleekfin-details-primary-button-fill,var(--jf-palette-primary-main,transparent))}'
            // Keep the primary hover on its own colours, or it picks up the secondary background.
            + '.paFgSavePrimary:hover{background:var(--sleekfin-details-primary-button-hover-fill,'
            + 'var(--sleekfin-details-primary-button-fill,var(--jf-palette-primary-main,#00a4dc)));'
            + 'color:var(--sleekfin-details-primary-button-text-color,var(--jf-palette-primary-contrastText,#fff));'
            + 'border-color:var(--sleekfin-details-primary-button-fill,var(--jf-palette-primary-main,transparent));'
            + 'filter:brightness(1.06)}'
            + '.paFgActions{display:flex;gap:8px;flex-wrap:wrap;justify-content:flex-end}'
            + '.paFgStatus{font-size:.85rem;min-height:1.2em;opacity:.95}'
            + '.paFgStatus.paFgError{color:#e55353}';

        document.head.appendChild(style);
    }

    /* ---------------- 详情页浮动按钮 ---------------- */

    function removeFab() {
        var fab = document.getElementById(FAB_ID);
        if (fab) {
            fab.parentElement && fab.parentElement.removeChild(fab);
        }
    }

    function createFab(itemId) {
        injectStyle();

        var button = document.createElement('button');
        button.id = FAB_ID;
        button.type = 'button';
        button.className = 'paFabButton';
        button.setAttribute('data-itemid', itemId);
        button.title = t('menu');
        button.setAttribute('aria-label', t('menu'));
        button.innerHTML = cameraIcon(20) + '<span>' + t('fab') + '</span>';
        inheritThemeVars(button);
        button.addEventListener('click', function () {
            openFrameGrab(itemId);
        });

        return button;
    }

    function ensureFab() {
        var itemId = currentItemId();
        if (!itemId) {
            removeFab();
            return;
        }

        var fab = document.getElementById(FAB_ID);
        if (fab && fab.getAttribute('data-itemid') === itemId) {
            return;
        }

        removeFab();

        if (checking[itemId]) {
            return;
        }

        checking[itemId] = true;
        isEnabled(itemId).then(function (enabled) {
            checking[itemId] = false;

            if (!enabled || currentItemId() !== itemId || document.getElementById(FAB_ID)) {
                return;
            }

            document.body.appendChild(createFab(itemId));
        });
    }

    /* ---------------- 原生「⋯」菜单项 ---------------- */

    function closeMenus() {
        try {
            document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, which: 27, bubbles: true }));
        } catch (error) {
            /* 忽略 */
        }

        Array.prototype.forEach.call(document.querySelectorAll('.MuiBackdrop-root'), function (backdrop) {
            backdrop.click();
        });
    }

    function buildMenuItem(template, itemId) {
        var li = document.createElement('li');
        li.className = template.className + ' ' + MENU_CLASS;
        li.setAttribute('role', 'menuitem');
        li.tabIndex = -1;
        li.innerHTML = template.innerHTML;

        var iconElement = li.querySelector('svg');
        if (iconElement) {
            var wrapper = document.createElement('span');
            wrapper.style.cssText = 'display:inline-flex;align-items:center;margin-right:12px';
            wrapper.innerHTML = cameraIcon(18);
            iconElement.parentElement ? iconElement.parentElement.replaceChild(wrapper, iconElement) : iconElement.replaceWith(wrapper);
        }

        var spans = li.querySelectorAll('span, p');
        var labelSet = false;
        for (var i = spans.length - 1; i >= 0; i--) {
            var span = spans[i];
            if (span.children.length === 0 && (span.textContent || '').trim().length > 0) {
                span.textContent = t('menu');
                labelSet = true;
                break;
            }
        }

        if (!labelSet) {
            li.textContent = t('menu');
        }

        li.addEventListener('click', function (event) {
            event.preventDefault();
            event.stopPropagation();
            closeMenus();
            setTimeout(function () {
                openFrameGrab(itemId);
            }, 120);
        });

        return li;
    }

    function injectMenuItems() {
        var itemId = currentItemId();
        if (!itemId) {
            return;
        }

        var menus = document.querySelectorAll('ul[role="menu"], .MuiMenu-list');
        Array.prototype.forEach.call(menus, function (menu) {
            if (!menu || menu.querySelector('.' + MENU_CLASS)) {
                return;
            }

            isEnabled(itemId).then(function (enabled) {
                if (!enabled || !document.body.contains(menu) || menu.querySelector('.' + MENU_CLASS)) {
                    return;
                }

                var template = menu.querySelector('li[role="menuitem"]') || menu.querySelector('li');
                if (!template) {
                    return;
                }

                menu.appendChild(buildMenuItem(template, itemId));
                log('menu item injected');
            });
        });
    }

    /* ---------------- 抽帧弹窗 ---------------- */

    function formatTime(seconds) {
        if (!isFinite(seconds)) {
            return '00:00';
        }

        var total = Math.max(0, Math.floor(seconds));
        var minutes = Math.floor(total / 60);
        var rest = total % 60;
        return (minutes < 10 ? '0' : '') + minutes + ':' + (rest < 10 ? '0' : '') + rest;
    }

    function openFrameGrab(itemId) {
        injectStyle();

        var dialog = document.createElement('dialog');
        dialog.className = 'paFgDialog';
        dialog.innerHTML = ''
            + '<div class="paFgBody">'
            + '<div class="paFgTitle">' + cameraIcon(20) + t('title') + '</div>'
            + '<video class="paFgVideo" controls muted preload="metadata"></video>'
            + '<div class="paFgBar">'
            + '<span class="paFgTime">00:00 / 00:00</span>'
            + '<span class="paFgRes"></span>'
            + '<button type="button" class="paFgBtn" data-delta="-1">' + icon('fast_rewind') + '-1s</button>'
            + '<button type="button" class="paFgBtn" data-delta="-0.1">' + icon('chevron_left') + '-0.1s</button>'
            + '<button type="button" class="paFgBtn" data-delta="0.1">' + icon('chevron_right') + '+0.1s</button>'
            + '<button type="button" class="paFgBtn" data-delta="1">' + icon('fast_forward') + '+1s</button>'
            + '</div>'
            + '<div class="paFgActions">'
            + '<button type="button" class="paFgBtn paFgSavePrimary" data-type="Primary">' + icon('image') + t('primary') + '</button>'
            + '<button type="button" class="paFgBtn" data-type="Backdrop">' + icon('landscape') + t('backdrop') + '</button>'
            + '<button type="button" class="paFgBtn paFgClose">' + icon('close') + t('close') + '</button>'
            + '</div>'
            + '<div class="paFgStatus"></div>'
            + '</div>';

        inheritThemeVars(dialog);

        var video = dialog.querySelector('.paFgVideo');
        var timeLabel = dialog.querySelector('.paFgTime');
        var resLabel = dialog.querySelector('.paFgRes');
        var status = dialog.querySelector('.paFgStatus');

        video.src = apiUrl('Videos/' + itemId + '/stream') + '?static=true&api_key=' + encodeURIComponent(authToken());

        function updateTime() {
            timeLabel.textContent = formatTime(video.currentTime) + ' / ' + formatTime(video.duration);
            if (video.videoWidth) {
                resLabel.textContent = video.videoWidth + '×' + video.videoHeight;
            }
        }

        function setStatus(message, isError) {
            status.textContent = message || '';
            status.className = 'paFgStatus' + (isError ? ' paFgError' : '');
        }

        video.addEventListener('timeupdate', updateTime);
        video.addEventListener('loadedmetadata', updateTime);
        video.addEventListener('seeked', updateTime);
        video.addEventListener('error', function () {
            setStatus(t('cannotPreview'), true);
        });

        Array.prototype.forEach.call(dialog.querySelectorAll('.paFgBar button'), function (button) {
            button.addEventListener('click', function () {
                var delta = parseFloat(button.getAttribute('data-delta'));
                if (!isNaN(delta)) {
                    video.currentTime = Math.max(0, Math.min(video.duration || 0, video.currentTime + delta));
                }
            });
        });

        function capture() {
            if (!video.videoWidth || !video.videoHeight) {
                return null;
            }

            var canvas = document.createElement('canvas');
            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;
            canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
            return canvas.toDataURL('image/jpeg', 0.92);
        }

        function refreshDetailImages() {
            Array.prototype.forEach.call(document.querySelectorAll('img'), function (image) {
                if (image.src && image.src.indexOf(itemId.replace(/-/g, '')) !== -1) {
                    image.src = image.src.split('?')[0] + '?tag=' + Date.now();
                }
            });
        }

        Array.prototype.forEach.call(dialog.querySelectorAll('button[data-type]'), function (button) {
            button.addEventListener('click', function () {
                var type = button.getAttribute('data-type');
                var dataUrl = capture();
                if (!dataUrl) {
                    setStatus(t('noFrame'), true);
                    return;
                }

                setStatus(t('uploading'));
                var base64 = dataUrl.substring(dataUrl.indexOf(',') + 1);
                apiPost(apiUrl('Items/' + itemId + '/Images/' + type), base64).then(function (response) {
                    if (response.status === 204 || response.ok) {
                        setStatus(type === 'Primary' ? t('savedPrimary') : t('savedBackdrop'));
                        refreshDetailImages();
                    } else {
                        setStatus(t('saveFailed') + 'HTTP ' + response.status, true);
                    }
                }).catch(function (error) {
                    setStatus(t('saveFailed') + error, true);
                });
            });
        });

        var closeButton = dialog.querySelector('.paFgClose');
        if (closeButton) {
            closeButton.addEventListener('click', function () {
                dialog.close();
            });
        }

        dialog.addEventListener('close', function () {
            video.pause();
            video.removeAttribute('src');
            video.load();
            dialog.parentElement && dialog.parentElement.removeChild(dialog);
        });

        document.body.appendChild(dialog);
        dialog.showModal();
        video.play().catch(function () {
            /* 自动播放被拦截时忽略，用户手动点播放即可 */
        });
    }

    /* ---------------- 启动 ---------------- */

    function bootstrap() {
        warmIconFont();
        log('frame grab loaded (' + LANG + ')');

        window.addEventListener('hashchange', ensureFab);
        window.addEventListener('popstate', ensureFab);

        document.addEventListener('click', function (event) {
            var target = event.target;
            if (!target || !target.closest) {
                return;
            }

            if (target.closest('button[aria-haspopup], [aria-haspopup="true"], .btnMoreCommands')) {
                setTimeout(injectMenuItems, 120);
                setTimeout(injectMenuItems, 400);
            }
        }, true);

        ensureFab();
        setTimeout(ensureFab, 500);
        setTimeout(ensureFab, 1500);

        // Re-resolve the variables after a theme switch.
        if (window.MutationObserver) {
            var themePending = null;
            new MutationObserver(function () {
                if (themePending) {
                    return;
                }

                themePending = setTimeout(function () {
                    themePending = null;
                    var fab = document.getElementById(FAB_ID);
                    if (fab) {
                        inheritThemeVars(fab);
                    }
                }, 300);
            }).observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'style', 'data-theme'] });
        }

        if (window.MutationObserver && document.body) {
            var pending = null;
            var observer = new MutationObserver(function () {
                if (pending) {
                    return;
                }

                pending = setTimeout(function () {
                    pending = null;
                    ensureFab();
                    injectMenuItems();
                }, 400);
            });
            observer.observe(document.body, { childList: true, subtree: true });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', bootstrap);
    } else {
        bootstrap();
    }
})();
