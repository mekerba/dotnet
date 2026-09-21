// ============================================================================
// Profile preview modal for /Profile/Edit.
//
// WHERE THIS RUNS: in the visitor's browser, on their machine. Not on the
// server. The server's involvement ended when it sent the HTML; everything
// below happens afterwards, and the only way back to the server is the
// fetch() call — a second, separate HTTP request.
//
// WHEN THIS RUNS: _Layout puts <script> tags at the end of <body>, so the
// browser reaches this file only after the whole document is parsed. That is
// why there is no DOMContentLoaded wrapper — the elements already exist.
//
// The outer function runs ONCE, at page load, and does nothing but attach a
// listener. The listener runs LATER, every time the modal opens. Those are two
// different moments, and keeping them straight is most of understanding
// client-side code.
// ============================================================================
(() => {
    "use strict";

    const modal = document.getElementById("profile-preview");
    if (!modal) return;

    const body = document.getElementById("profile-preview-body");
    const url = modal.dataset.summaryUrl;
    if (!body || !url) return;

    // Which fields to show, in order, and what to call them. camelCase on the
    // right because System.Text.Json renames C#'s FirstName to "firstName" on
    // the way out — see the note in ProfileController.Summary.
    const FIELDS = [
        ["Email", "email"],
        ["Title", "title"],
        ["First name", "firstName"],
        ["Last name", "lastName"],
        ["Gender", "gender"],
        ["Date of birth", "dateBirth"],
        ["Place of birth", "placeBirth"],
        ["City", "city"],
        ["Country", "country"],
        ["Nationality", "nationality"],
        ["Position", "position"],
        ["Institution", "institution"],
        ["Biography", "biography"],
    ];

    // ------------------------------------------------------------------
    // "show.bs.modal" fires every time the modal OPENS, which is what we
    // want: reopening it re-reads the database rather than showing a stale
    // copy from the first open.
    //
    // Bootstrap raises this itself. Listening for it rather than for the
    // button's click also means the modal stays correct if it is ever opened
    // some other way.
    // ------------------------------------------------------------------
    modal.addEventListener("show.bs.modal", loadSummary);

    async function loadSummary() {
        showSpinner();

        try {
            // The second request. The address bar does not change, the page
            // does not reload, and the server has no idea this is related to
            // the page it rendered earlier — to it, this is just another GET
            // carrying the same auth cookie.
            const response = await fetch(url, {
                headers: { "Accept": "application/json" },
            });

            if (!response.ok) {
                showError(`The server answered ${response.status}.`);
                return;
            }

            // --------------------------------------------------------------
            // THE EXPIRED-SESSION TRAP.
            //
            // ProfileController is [Authorize]. When the auth cookie expires,
            // the framework answers with 302 -> /Account/Login, and fetch
            // FOLLOWS redirects silently. So this code sees 200 OK carrying an
            // HTML login page, and response.json() throws a parse error that
            // looks nothing like "you are logged out".
            //
            // Checking the content type turns it into a condition you can act
            // on. The server-side fix is an OnRedirectToLogin handler in
            // Program.cs that returns 401 when the request asked for JSON.
            // --------------------------------------------------------------
            const contentType = response.headers.get("content-type") ?? "";
            if (!contentType.includes("application/json")) {
                showError("Your session has expired. Reload the page and sign in again.");
                return;
            }

            render(await response.json());
        } catch (error) {
            console.error(error);
            showError("Could not reach the server.");
        }
    }

    function render(profile) {
        const list = document.createElement("dl");
        list.className = "row mb-0";

        let shown = 0;

        for (const [label, key] of FIELDS) {
            const value = profile[key];
            if (value === null || value === undefined || value === "") continue;

            const term = document.createElement("dt");
            term.className = "col-sm-4 text-body-secondary fw-normal";
            term.textContent = label;

            const definition = document.createElement("dd");
            definition.className = "col-sm-8";

            // ----------------------------------------------------------
            // textContent, NEVER innerHTML.
            //
            // Biography is free text typed by a user. Assigning it to
            // innerHTML would execute any <script> inside it — stored XSS.
            // textContent inserts it as text, always.
            //
            // Razor escapes by default and Django autoescapes; the moment you
            // build DOM by hand in JavaScript, that protection is gone and
            // this is what replaces it.
            // ----------------------------------------------------------
            definition.textContent = value;

            list.append(term, definition);
            shown++;
        }

        body.replaceChildren();

        // A status line first: whether the profile is validated, and when it
        // last changed.
        const status = document.createElement("p");
        status.className = profile.isValidated
            ? "alert alert-success py-2"
            : "alert alert-warning py-2";
        status.textContent = profile.isValidated
            ? "This profile has been validated."
            : "This profile is awaiting validation.";
        body.append(status);

        if (shown === 0) {
            const empty = document.createElement("p");
            empty.className = "text-body-secondary mb-0";
            empty.textContent = "Nothing saved yet — fill the form in and save.";
            body.append(empty);
        } else {
            body.append(list);
        }

        if (profile.lastUpdate) {
            const stamp = document.createElement("p");
            stamp.className = "text-body-secondary small mt-3 mb-0";
            // The server sends ISO 8601; the browser formats it in the
            // visitor's own locale and time zone. Razor would have formatted
            // it in the SERVER's.
            stamp.textContent = `Last updated ${new Date(profile.lastUpdate).toLocaleString()}`;
            body.append(stamp);
        }
    }

    function showSpinner() {
        body.replaceChildren();

        const wrap = document.createElement("div");
        wrap.className = "text-center text-body-secondary py-4";

        const spinner = document.createElement("div");
        spinner.className = "spinner-border spinner-border-sm";
        spinner.role = "status";

        const label = document.createElement("span");
        label.className = "ms-2";
        label.textContent = "Loading…";

        wrap.append(spinner, label);
        body.append(wrap);
    }

    function showError(message) {
        body.replaceChildren();

        const alert = document.createElement("div");
        alert.className = "alert alert-danger mb-0";
        alert.setAttribute("role", "alert");
        alert.textContent = message;

        body.append(alert);
    }

    // ------------------------------------------------------------------
    // IF YOU MAKE AN ENDPOINT THAT WRITES, IT NEEDS A TOKEN.
    //
    // GET is exempt from antiforgery. A POST endpoint is not: MVC validates
    // Django's csrfmiddlewaretoken equivalent, and a fetch() without it gets a
    // 400 that says nothing useful. The form on this page already renders a
    // hidden field, so:
    //
    //   const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    //
    //   await fetch(url, {
    //       method: "POST",
    //       headers: {
    //           "Content-Type": "application/json",
    //           "RequestVerificationToken": token,
    //       },
    //       body: JSON.stringify({ ... }),
    //   });
    //
    // and the action takes [HttpPost] [ValidateAntiForgeryToken] plus a
    // [FromBody] parameter. Django: @csrf_protect and the X-CSRFToken header.
    // ------------------------------------------------------------------
})();
