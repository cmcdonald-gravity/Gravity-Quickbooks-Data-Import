(function () {
  const qs = new URLSearchParams(location.search);
  const isConnected = qs.get("connected") === "1";
  const realmId = qs.get("realmId") || "";

  const $ = (id) => document.getElementById(id);

  const titleEl        = $("title");
  const connectState   = $("connectState");
  const connectedState = $("connectedState");
  const statusEl       = $("status");
  const importBtn      = $("importBtn");
  const importStatus   = $("importStatus");
  const resultsDiv     = $("results");

  function escapeHtml(s) {
    return String(s ?? "").replace(/[&<>"']/g, (m) => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#039;"
    }[m]));
  }

  function formatCurrency(n) {
    const v = Number(n || 0);
    return v.toLocaleString(undefined, { style: "currency", currency: "USD" });
  }

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

  function renderBillsTable(rows) {
    resultsDiv.innerHTML = "";

    if (!Array.isArray(rows) || rows.length === 0) {
      resultsDiv.innerHTML = `<p class="muted">No bills found.</p>`;
      return;
    }

    // Optional: simple ordering
    rows.sort((a, b) => (a.TxnDate ?? "").localeCompare(b.TxnDate ?? ""));

    const table = document.createElement("table");
    table.className = "data";

    const thead = document.createElement("thead");
    thead.innerHTML = `
      <tr>
        <th>Doc #</th>
        <th>Date</th>
        <th>Vendor</th>
        <th>Total</th>
        <th>Memo</th>
        <th>Id</th>
      </tr>
    `;
    table.appendChild(thead);

    const tbody = document.createElement("tbody");
    for (const r of rows) {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td>${escapeHtml(r.DocNumber)}</td>
        <td>${escapeHtml(r.TxnDate)}</td>
        <td>${escapeHtml(r.VendorName)}</td>
        <td>${formatCurrency(r.TotalAmt)}</td>
        <td>${escapeHtml(r.Memo)}</td>
        <td>${escapeHtml(r.Id)}</td>
      `;
      tbody.appendChild(tr);
    }

    table.appendChild(tbody);
    resultsDiv.appendChild(table);
  }

  async function fetchBills() {
    if (!realmId) {
      importStatus.textContent = "Missing realmId. Please reconnect.";
      return;
    }

    resultsDiv.innerHTML = "";
    importStatus.textContent = "Loading bills…";
    importBtn.disabled = true;

    try {
      const url = `/api/bills?realmId=${encodeURIComponent(realmId)}`;
      const res = await fetch(url, {
        headers: { "Accept": "application/json" }
      });

      if (!res.ok) {
        const text = await res.text();
        throw new Error(`Server error ${res.status}: ${text}`);
      }

      const rows = await res.json();
      importStatus.textContent = `Loaded ${rows.length} bills.`;
      renderBillsTable(rows);

    } catch (err) {
      console.error(err);
      importStatus.textContent = `Failed to load bills: ${err.message}`;
    } finally {
      importBtn.disabled = false;
    }
  }

  // Startup logic
  if (isConnected && realmId) {
    showConnected();
    importBtn.addEventListener("click", fetchBills);
  } else {
    showConnect();
    $("connectQB")?.addEventListener("click", () => {
      if (statusEl) statusEl.textContent = "Redirecting to QuickBooks…";
      window.location.assign("/auth/quickbooks");
    });
  }
})();