(function () {

  const API = "https://localhost:5148";

  const qs = new URLSearchParams(location.search);
  const connected = qs.get("connected") === "1";
  const realmId = qs.get("realmId") || "";

  const get = (id) => document.getElementById(id);

  const connectState = get("connectState");
  const connectedState = get("connectedState");
  const title = get("title");

  if (connected && realmId) {

    // ----- CONNECTED -----
    title.textContent = "QuickBooks Connected";
    connectState.hidden = true;
    connectedState.hidden = false;

    get("realmIdText").textContent = realmId;
    get("realmIdInput").value = realmId;

  } else {

    // ----- NOT CONNECTED -----
    title.textContent = "QuickBooks Connection";
    connectState.hidden = false;
    connectedState.hidden = true;

    get("connectQB").addEventListener("click", () => {
      get("status").textContent = "Redirecting to QuickBooks…";
      window.location.href = API + "/auth/quickbooks";
    });
  }

})();