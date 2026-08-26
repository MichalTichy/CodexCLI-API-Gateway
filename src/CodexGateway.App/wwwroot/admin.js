document.addEventListener("keydown", event => {
    if (event.key !== "Tab") {
        return;
    }

    const trap = event.target.closest("[data-focus-trap]");
    if (!trap) {
        return;
    }

    const controls = [...trap.querySelectorAll("button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])")]
        .filter(control => control.getClientRects().length > 0);
    if (controls.length === 0) {
        return;
    }

    const first = controls[0];
    const last = controls[controls.length - 1];
    if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
    }
});

globalThis.adminUi = {
    focus(id) {
        document.getElementById(id)?.focus();
    }
};
