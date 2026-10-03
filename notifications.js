// Jot's dependency-free adapter for the shared Web Sonner presentation.
// Content is always text, and notifications never take focus when shown.
(() => {
  if (window.JotToast) return;
  const notices = new Map();
  const icons = {
    success: '<circle cx="12" cy="12" r="9"/><path d="m8 12 3 3 5-6"/>',
    error: '<circle cx="12" cy="12" r="9"/><path d="M12 8v5m0 3h.01"/>',
    warning: '<path d="m10.3 3.9-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.7-3.1l-8-14a2 2 0 0 0-3.4 0Z"/><path d="M12 9v4m0 4h.01"/>',
    info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6m0-10h.01"/>',
    loading: '<path d="M21 12a9 9 0 1 1-6.2-8.55"/>',
  };
  let viewport, list, polite, urgent, sequence = 0, frame = 0, focusFrame = 0;
  const svg = (paths) => {
    const element = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    element.setAttribute('viewBox', '0 0 24 24');
    element.setAttribute('aria-hidden', 'true');
    element.innerHTML = paths; // Only the constant icon paths above, never user content.
    return element;
  };
  const visible = (element) => {
    const rectangle = element.getBoundingClientRect();
    return rectangle.width > 0 && rectangle.height > 0 && rectangle.bottom > 0 && rectangle.top < innerHeight && getComputedStyle(element).visibility !== 'hidden';
  };
  function ensureViewport() {
    if (viewport) return;
    viewport = document.createElement('section');
    viewport.id = 'jotToaster';
    viewport.className = 'jot-toaster';
    viewport.setAttribute('aria-label', 'Notifications');
    viewport.setAttribute('dir', 'ltr');
    if (typeof viewport.showPopover === 'function') viewport.setAttribute('popover', 'manual');
    list = document.createElement('ol');
    list.className = 'jot-toast-list';
    polite = document.createElement('div');
    polite.className = 'jot-toast-announcer';
    polite.setAttribute('role', 'status');
    polite.setAttribute('aria-live', 'polite');
    polite.setAttribute('aria-atomic', 'true');
    urgent = document.createElement('div');
    urgent.className = 'jot-toast-announcer';
    urgent.setAttribute('role', 'alert');
    urgent.setAttribute('aria-live', 'assertive');
    urgent.setAttribute('aria-atomic', 'true');
    viewport.append(list, polite, urgent);
    document.body.append(viewport);
    viewport.addEventListener('keydown', (event) => {
      if (event.key !== 'Escape') return;
      const notice = notices.get(event.target.closest('[data-toast-id]')?.dataset.toastId);
      if (!notice) return;
      event.preventDefault();
      event.stopPropagation();
      dismiss(notice.id);
    });
    new MutationObserver(schedulePlacement).observe(document.body, { subtree: true, attributes: true, attributeFilter: ['open', 'hidden'] });
    new ResizeObserver(schedulePlacement).observe(document.body);
    new ResizeObserver(schedulePlacement).observe(list);
    window.addEventListener('resize', schedulePlacement);
    // Reconcile after the browser completes its focus change, not while blur
    // is being dispatched and activeElement may still describe the old control.
    const changedFocus = () => {
      if (!notices.size) return;
      if (focusFrame) return;
      focusFrame = requestAnimationFrame(() => {
        focusFrame = 0;
        for (const notice of notices.values()) {
          if (notice.element.contains(document.activeElement)) pause(notice, 'focus');
          else resume(notice, 'focus');
        }
        render();
      });
    };
    document.addEventListener('focusin', changedFocus, true);
    document.addEventListener('focusout', changedFocus, true);
    window.addEventListener('focus', changedFocus);
    document.addEventListener('visibilitychange', () => {
      for (const notice of notices.values()) {
        if (document.hidden) pause(notice, 'document');
        else resume(notice, 'document');
      }
    });
  }
  function schedulePlacement() {
    if (!notices.size && viewport?.hidden) return;
    if (!frame) frame = requestAnimationFrame(() => { frame = 0; place(); });
  }
  function place() {
    if (!viewport) return;
    if (!notices.size) {
      if (viewport.hidePopover && viewport.matches(':popover-open')) viewport.hidePopover();
      if (!viewport.hidden) viewport.hidden = true;
      return;
    }
    const dialog = [...document.querySelectorAll('dialog[open]')].filter(element => element.matches(':modal')).at(-1);
    const parent = dialog || document.body;
    if (viewport.parentElement !== parent) parent.append(viewport);
    let bottom = 14, top = 14;
    // Keep desktop notifications bottom-right and clear of the writing footer.
    for (const footer of document.querySelectorAll('.quiet-footer,.home-footer,.image-footer')) {
      if (footer.classList.contains('document-toolbar')) continue;
      if (visible(footer)) bottom = Math.max(bottom, innerHeight - footer.getBoundingClientRect().top + 12);
    }
    for (const header of document.querySelectorAll('.handle,.workspace-header,.image-handle,.document-toolbar')) {
      if (visible(header)) top = Math.max(top, header.getBoundingClientRect().bottom + 14);
    }
    const available = Math.max(48, innerHeight - top - bottom);
    viewport.style.setProperty('--toast-bottom', `${bottom}px`);
    viewport.style.setProperty('--toast-max-height', `${available}px`);
    viewport.dataset.modal = String(!!dialog);
    if (notices.size && viewport.showPopover && !viewport.matches(':popover-open')) {
      try { viewport.showPopover(); } catch { /* A navigation may have detached it. */ }
    } else if (!notices.size && viewport.hidePopover && viewport.matches(':popover-open')) viewport.hidePopover();
    if (viewport.hidden) viewport.hidden = false;
  }
  function pause(notice, reason) {
    notice.pauses.add(reason);
    if (notice.timer) {
      notice.remaining = Math.max(0, notice.remaining - (performance.now() - notice.started));
      clearTimeout(notice.timer);
      notice.timer = 0;
    }
  }
  function resume(notice, reason) {
    notice.pauses.delete(reason);
    startTimer(notice);
  }
  function startTimer(notice) {
    // Detached controls can still finish async actions or emit focusout. They
    // must never arm a timer that later dismisses a newer notice with this ID.
    if (notices.get(notice.id) !== notice || !notice.visible || notice.timer || notice.pauses.size || !Number.isFinite(notice.remaining)) return;
    notice.started = performance.now();
    notice.timer = setTimeout(() => dismiss(notice.id), notice.remaining);
  }
  function announce(notice) {
    if (!notice.visible) return;
    const announcer = notice.type === 'error' ? urgent : polite;
    announcer.textContent = '';
    requestAnimationFrame(() => {
      if (notices.has(notice.id) && notice.visible) announcer.textContent = [notice.message, notice.description].filter(Boolean).join('. ');
    });
  }
  function build(notice) {
    const item = document.createElement('li');
    item.className = 'jot-toast';
    item.dataset.toastId = notice.id;
    item.dataset.type = notice.type;
    item.tabIndex = 0;
    item.setAttribute('aria-label', `${notice.type === 'error' ? 'Error: ' : ''}${notice.message}. Press Escape to dismiss.`);
    const icon = document.createElement('span');
    icon.className = 'jot-toast-icon';
    icon.append(svg(icons[notice.type] || icons.info));
    const content = document.createElement('div');
    content.className = 'jot-toast-content';
    const title = document.createElement('div');
    title.className = 'jot-toast-title';
    title.textContent = notice.message;
    const description = document.createElement('div');
    description.className = 'jot-toast-description';
    description.textContent = notice.description;
    description.hidden = !notice.description;
    content.append(title, description);
    item.append(icon, content);
    if (notice.action && typeof notice.action.onClick === 'function' && notice.action.label) {
      const action = document.createElement('button');
      action.type = 'button';
      action.className = 'jot-toast-action';
      action.textContent = String(notice.action.label);
      action.addEventListener('click', async () => {
        if (action.disabled) return;
        pause(notice, 'action');
        action.disabled = true;
        action.setAttribute('aria-busy', 'true');
        action.textContent = String(notice.action.pendingLabel || 'Working…');
        try {
          await notice.action.onClick();
          // A callback may have intentionally updated the same notice.
          if (notices.get(notice.id) === notice && notice.element === item) dismiss(notice.id);
        } catch (error) {
          if (notices.get(notice.id) === notice) show(error?.message || 'That action could not be completed.', { id: notice.id, type: 'error' });
        } finally { resume(notice, 'action'); render(); }
      });
      item.append(action);
    }
    item.addEventListener('pointerenter', () => pause(notice, 'hover'));
    item.addEventListener('pointerleave', () => resume(notice, 'hover'));
    item.addEventListener('focusin', () => pause(notice, 'focus'));
    return item;
  }
  function render() {
    const all = [...notices.values()];
    // Keep controls under the user's keyboard focus (or running an action) in
    // place. New failures must otherwise be visible even behind sticky errors.
    const protectedNotices = all.filter(notice => notice.element.contains(document.activeElement) || notice.pauses.has('action'));
    const failures = all.filter(notice => notice.type === 'error').sort((a, b) => b.priority - a.priority);
    const shownNotices = new Set([...protectedNotices, ...failures, ...all].filter((notice, index, ordered) => ordered.indexOf(notice) === index).slice(0, 3));
    for (const notice of notices.values()) {
      const shown = shownNotices.has(notice);
      const newlyShown = shown && !notice.visible;
      notice.visible = shown;
      if (notice.element.hidden === shown) notice.element.hidden = !shown;
      if (!notice.element.isConnected) list.append(notice.element);
      if (shown) { startTimer(notice); if (newlyShown) announce(notice); }
      else pause(notice, 'queue');
      if (shown) resume(notice, 'queue');
    }
    const empty = notices.size === 0;
    if (viewport.hidden !== empty) viewport.hidden = empty;
    place();
  }
  function show(message, options = {}) {
    const text = String(message ?? '').trim();
    if (!text) return null;
    ensureViewport();
    const type = Object.hasOwn(icons, options.type) ? options.type : 'info';
    const description = options.description == null ? '' : String(options.description);
    const duplicate = options.id == null ? [...notices.values()].find(item => item.type === type && item.message === text && item.description === description) : null;
    const priority = ++sequence;
    const id = String(options.id ?? duplicate?.id ?? `notification-${priority}`);
    const previous = notices.get(id);
    const duration = options.duration ?? (type === 'loading' ? Infinity : type === 'error' ? 8000 : 5000);
    const notice = {
      id, priority, type, message: text, description, action: options.action, onDismiss: options.onDismiss,
      remaining: duration === Infinity || duration === 0 ? Infinity : Math.max(250, Number(duration) || 5000),
      pauses: new Set(document.hidden ? ['document'] : []), timer: 0, started: 0,
      visible: previous?.visible || false, returnFocus: previous?.returnFocus || document.activeElement,
    };
    notice.element = build(notice);
    if (previous) {
      clearTimeout(previous.timer);
      const focusClass = previous.element.contains(document.activeElement) ? document.activeElement.className : null;
      previous.element.replaceWith(notice.element);
      if (focusClass) (focusClass === 'jot-toast-action' ? notice.element.querySelector('.jot-toast-action') || notice.element : notice.element).focus({ preventScroll: true });
      if (notice.element.matches(':hover')) notice.pauses.add('hover');
      if (notice.element.contains(document.activeElement)) notice.pauses.add('focus');
    }
    notices.set(id, notice);
    render();
    if (previous) announce(notice);
    return id;
  }
  function dismiss(id) {
    if (id == null) {
      for (const key of [...notices.keys()]) dismiss(key);
      return;
    }
    const notice = notices.get(String(id));
    if (!notice) return;
    const ownedFocus = notice.element.contains(document.activeElement);
    clearTimeout(notice.timer);
    notices.delete(notice.id);
    notice.element.remove();
    render();
    if (ownedFocus) {
      const next = [...notices.values()].find(item => item.visible)?.element;
      const target = next || (notice.returnFocus?.isConnected && visible(notice.returnFocus) ? notice.returnFocus : null);
      target?.focus({ preventScroll: true });
    }
    if (typeof notice.onDismiss === 'function') notice.onDismiss(notice.id);
  }
  window.JotToast = Object.freeze({
    show, dismiss,
    has: (id) => notices.has(String(id)),
    get: (id) => {
      const notice = notices.get(String(id));
      return notice ? Object.freeze({ id: notice.id, type: notice.type, message: notice.message, description: notice.description, visible: notice.visible }) : null;
    },
    dismissType: (type) => {
      for (const notice of [...notices.values()]) if (notice.type === type) dismiss(notice.id);
    },
    loading: (message, options) => show(message, { ...options, type: 'loading' }),
    success: (message, options) => show(message, { ...options, type: 'success' }),
    error: (message, options) => show(message, { ...options, type: 'error' }),
    info: (message, options) => show(message, { ...options, type: 'info' }),
    warning: (message, options) => show(message, { ...options, type: 'warning' }),
  });
})();
