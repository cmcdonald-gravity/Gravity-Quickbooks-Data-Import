(function () {
  const qs = new URLSearchParams(location.search);
  const isConnected = qs.get("connected") === "1";
  const realmId = qs.get("realmId") || "";

  const $ = (id) => document.getElementById(id);

  const titleEl            = $("title");
  const connectState       = $("connectState");
  const connectedState     = $("connectedState");
  const statusEl           = $("status");

  // Bills
  const importBtn          = $("importBtn");
  const downloadBtn        = $("downloadBtn");
  const importStatus       = $("importStatus");

  // Accounts (COA)
  const accountsImportBtn  = $("accountsImportBtn");
  const accountsDownloadBtn= $("accountsDownloadBtn");
  const accountsStatus     = $("accountsStatus");

  function showConnect() {
    titleEl.textContent = "QuickBooks Connection";
    connectState.hidden = false;
    connectedState.hidden = true;
  }

  function showConnected() {
    titleEl.textContent = "QuickBooks Connected";
    connectState.hidden = true;
    connectedState.hidden = false;
  }

  async function fetchBills() {
    if (!realmId) {
      importStatus.textContent = "Missing realmId. Please reconnect.";
      return;
    }
    importStatus.textContent = "Loading bills…";
    importBtn.disabled = true;
    downloadBtn.style.display = "none";
    try {
      const res = await fetch(`/api/bills?realmId=${encodeURIComponent(realmId)}`, {
        headers: { "Accept": "application/json" }
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(`Server error ${res.status}: ${text}`);
      }
      const rows = await res.json();
      importStatus.textContent = `Loaded ${Array.isArray(rows) ? rows.length : 0} bills.`;
      downloadBtn.style.display = "inline-block";
      downloadBtn.onclick = () =>
        window.location.assign(`/api/export-vouchers?realmId=${encodeURIComponent(realmId)}`);
    } catch (err) {
      console.error(err);
      importStatus.textContent = `Failed to load bills: ${err.message}`;
    } finally {
      importBtn.disabled = false;
    }
  }

  async function fetchAccounts() {
    if (!realmId) {
      accountsStatus.textContent = "Missing realmId. Please reconnect.";
      return;
    }
    accountsStatus.textContent = "Loading chart of accounts…";
    accountsImportBtn.disabled = true;
    accountsDownloadBtn.style.display = "none";
    try {
      const res = await fetch(`/api/accounts?realmId=${encodeURIComponent(realmId)}`, {
        headers: { "Accept": "application/json" }
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(`Server error ${res.status}: ${text}`);
      }
      const rows = await res.json();
      accountsStatus.textContent = `Loaded ${Array.isArray(rows) ? rows.length : 0} accounts.`;
      accountsDownloadBtn.style.display = "inline-block";
      accountsDownloadBtn.onclick = () =>
        window.location.assign(`/api/export-accounts?realmId=${encodeURIComponent(realmId)}`);
    } catch (err) {
      console.error(err);
      accountsStatus.textContent = `Failed to load accounts: ${err.message}`;
    } finally {
      accountsImportBtn.disabled = false;
    }
  }

  if (isConnected && realmId) {
    showConnected();
    importBtn.addEventListener("click", fetchBills);
    accountsImportBtn.addEventListener("click", fetchAccounts);
  } else {
    showConnect();
    $("connectQB")?.addEventListener("click", () => {
      if (statusEl) statusEl.textContent = "Redirecting to QuickBooks…";
      window.location.assign("/auth/quickbooks");
    });
  }
})();