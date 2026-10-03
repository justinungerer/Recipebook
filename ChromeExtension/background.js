const API = "http://127.0.0.1:47831";
const TOKEN = "cba61e4a-f2c9-4b40-a691-3fd428d775b6";
const headers = {
  "X-RecipeTool-Token": TOKEN,
  "X-RecipeTool-Version": chrome.runtime.getManifest().version
};

async function pollDesktopApp() {
  const status = await fetch(`${API}/api/status`, {
    cache: "no-store",
    headers
  });
  if (!status.ok) throw new Error(`Desktop app status check failed (${status.status}).`);

  const request = await fetch(`${API}/api/capture-request`, {
    cache: "no-store",
    headers
  });
  if (request.status === 204) return { running: true };
  if (!request.ok) throw new Error(`Capture request check failed (${request.status}).`);
  return { running: true, ...(await request.json()) };
}

async function sendCapturedRecipe(requestId, recipe) {
  const response = await fetch(`${API}/api/capture/${encodeURIComponent(requestId)}`, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify(recipe)
  });
  if (response.status === 409) return { ok: false, alreadyHandled: true };
  if (!response.ok) throw new Error(`The desktop app rejected the recipe (${response.status}).`);
  return { ok: true };
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (sender.id !== chrome.runtime.id || !sender.tab) return false;

  let operation;
  if (message?.action === "poll") {
    operation = pollDesktopApp();
  } else if (message?.action === "captured"
    && typeof message.requestId === "string"
    && message.recipe
    && typeof message.recipe === "object") {
    operation = sendCapturedRecipe(message.requestId, message.recipe);
  } else {
    return false;
  }

  operation
    .then(sendResponse)
    .catch(error => {
      console.error("Barb's Recipe Book connection error:", error);
      sendResponse({ ok: false, error: error.message });
    });
  return true;
});
