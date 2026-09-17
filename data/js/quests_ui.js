(function () {
    'use strict';
    var ws = null;
    var model = null;
    var currentQuest = '';
    var currentInteraction = '';

    function root() { return document.getElementById('questApp'); }
    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>\"']/g, function (c) {
            return ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '\"':'&quot;', "'":'&#39;' })[c];
        });
    }

    function send(obj) {
        if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(obj));
    }

    function connect() {
        try {
            ws = new WebSocket('ws://localhost:8085/');
            ws.onopen = function () { send({ command: 'quest_ping' }); };
            ws.onmessage = function (ev) {
                try {
                    var data = JSON.parse(ev.data);
                    if (data.command === 'quest_state') applyState(data);
                    else if (data.command === 'quest_error') showError(data.text);
                } catch (e) { console.warn('[QUEST] bad packet', e); }
            };
            ws.onclose = function () { setTimeout(connect, 1500); };
            ws.onerror = function () { try { ws.close(); } catch (e) {} };
        } catch (e) { setTimeout(connect, 1500); }
    }

    function applyState(data) {
        model = data;
        var app = root();
        if (!app) return;
        app.classList.toggle('paused', data.paused === true);
        if (!data.paused) return;
        renderInteractions();
        renderQuests();
        renderInventory();
        if (data.dialogue) renderDialogue(data.dialogue);
        else if (currentInteraction) clearDialogue();
        if (!currentInteraction && data.points && data.points.length) {
            var p = data.points.find(function (x) { return x.Interactive; });
            if (p) selectInteraction(p.QuestId, p.InteractionId);
        }
        if (!data.points || !data.points.length) clearDialogue();
    }

    function renderInteractions() {
        var el = document.getElementById('interactionList');
        if (!el || !model) return;
        var pts = (model.points || []).filter(function (p) { return p.Interactive && p.MinimapVisible; });
        if (!pts.length) { el.innerHTML = '<div class="muted">Нет доступных интерактивов</div>'; return; }
        el.innerHTML = pts.map(function (p) {
            var active = p.QuestId === currentQuest && p.InteractionId === currentInteraction;
            var icon = markerIcon(p.Marker);
            return '<button class="sideItem' + (active ? ' selected' : '') + '" data-q="' + esc(p.QuestId) + '" data-i="' + esc(p.InteractionId) + '">' +
                   '<img src="' + icon + '" onerror="this.style.display=\'none\'">' + esc(p.Name) + '</button>';
        }).join('');
        el.querySelectorAll('.sideItem').forEach(function (b) {
            b.onclick = function () { selectInteraction(b.dataset.q, b.dataset.i); };
        });
    }

    function renderQuests() {
        var el = document.getElementById('questList');
        if (!el || !model) return;
        var all = [].concat(model.activeQuests || [], model.archiveQuests || []);
        if (!all.length) { el.innerHTML = '<div class="muted">Нет активных квестов</div>'; return; }
        el.innerHTML = all.map(function (q) {
            var cls = 'questItem ' + (q.status === 'Active' ? 'active' : 'archive');
            return '<button class="' + cls + '" data-q="' + esc(q.id) + '"><strong>' + esc(q.title) + '</strong><span>' + esc(q.status) + '</span></button>';
        }).join('');
        el.querySelectorAll('.questItem').forEach(function (b) {
            b.onclick = function () { showQuestDetail(b.dataset.q); };
        });
    }

    function showQuestDetail(id) {
        var q = [].concat((model && model.activeQuests) || [], (model && model.archiveQuests) || []).find(function (x) { return x.id === id; });
        if (!q) return;
        var center = document.getElementById('dialogText');
        var speaker = document.getElementById('dialogSpeaker');
        var options = document.getElementById('dialogOptions');
        if (speaker) speaker.textContent = q.title;
        if (center) center.textContent = q.description;
        if (options) options.innerHTML = (q.rewards || []).map(function (r) {
            var color = r.color ? ' style="color:' + esc(r.color) + '"' : '';
            return '<div class="rewardLine"' + color + '>' + esc(r.display || r.id) + ' x' + esc(r.amount) + '</div>';
        }).join('');
        currentQuest = id;
        currentInteraction = '';
        renderInteractions();
    }

    function renderInventory() {
        var el = document.getElementById('inventory');
        if (!el || !model) return;
        var items = model.inventory || [];
        el.innerHTML = '<span class="inventoryTitle">Инвентарь</span> ' + (items.length ? items.map(function (x) { return '<span class="inventoryItem">' + esc(x.name) + ' ×' + esc(x.amount) + '</span>'; }).join(' ') : '<span class="muted">пусто</span>');
    }

    function renderDialogue(node) {
        var speaker = document.getElementById('dialogSpeaker');
        var text = document.getElementById('dialogText');
        var img = document.getElementById('dialogImage');
        var opts = document.getElementById('dialogOptions');
        if (speaker) speaker.textContent = node.speaker || '';
        if (text) text.textContent = node.text || '';
        if (img) { img.src = node.image || ''; img.style.display = node.image ? 'block' : 'none'; }
        if (opts) {
            opts.innerHTML = (node.options || []).map(function (o, i) {
                return '<button class="dialogOption" data-index="' + i + '" ' + (o.enabled === false ? 'disabled' : '') + '>' + esc(o.text) + '</button>';
            }).join('');
            opts.querySelectorAll('.dialogOption').forEach(function (b) {
                b.onclick = function () {
                    send({ command: 'quest_dialog_option', questId: currentQuest, interaction: currentInteraction, index: Number(b.dataset.index) });
                };
            });
        }
    }

    function clearDialogue() {
        var s = document.getElementById('dialogSpeaker');
        var t = document.getElementById('dialogText');
        var o = document.getElementById('dialogOptions');
        if (s) s.textContent = '';
        if (t) t.textContent = 'Выберите интерактив слева.';
        if (o) o.innerHTML = '';
    }

    function selectInteraction(qid, iid) {
        if (!model || model.paused !== true) return;
        currentQuest = qid; currentInteraction = iid;
        send({ command: 'quest_select_interaction', questId: qid, id: iid });
        renderInteractions();
    }

    function markerIcon(marker) {
        if (marker === 'yellow_exclamation') return 'editor_static_data/icons/quest_exclamation_yellow.svg';
        if (marker === 'yellow_question') return 'editor_static_data/icons/quest_question_yellow.svg';
        if (marker === 'gray_question') return 'editor_static_data/icons/quest_question_gray.svg';
        return '';
    }

    function showError(text) {
        var n = document.getElementById('overlayError');
        if (!n) return;
        n.textContent = text || 'Ошибка'; n.classList.add('show');
        setTimeout(function () { n.classList.remove('show'); }, 2500);
    }

    function resize() {
        document.documentElement.style.setProperty('--vw', window.innerWidth + 'px');
        document.documentElement.style.setProperty('--vh', window.innerHeight + 'px');
    }

    document.addEventListener('DOMContentLoaded', function () { resize(); window.addEventListener('resize', resize); connect(); });
})();