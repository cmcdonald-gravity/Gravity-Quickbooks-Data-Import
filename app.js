(function () {
  const btn = document.getElementById("connectQB");
  const statusEl = document.getElementById("status");

  // Frontend never builds the Intuit URL and never stores client secrets.
  // It simply asks YOUR server to start the OAuth flow.
  // Change the path if your server uses a different route.
  const START_OAUTH_PATH = "/auth/quickbooks";

  btn?.addEventListener("click", () => {
    if (statusEl) statusEl.textContent = "Redirecting to QuickBooks…";
    // Full page navigation so cookies/session are included and redirects work
    window.location.assign(START_OAUTH_PATH);
  });
})();