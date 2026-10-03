(() => {
  const POLL_MS = 1800;
  let busy = false;

  function normalizeText(value) {
    return String(value ?? "").replace(/\s+/g, " ").trim();
  }

  function typeIsRecipe(value) {
    const types = Array.isArray(value) ? value : [value];
    return types.some(type => String(type).toLowerCase().endsWith("recipe"));
  }

  function flattenStructuredText(value) {
    if (typeof value === "string") return [normalizeText(value)].filter(Boolean);
    if (Array.isArray(value)) return value.flatMap(flattenStructuredText);
    if (!value || typeof value !== "object") return [];
    if (typeof value.text === "string") return [normalizeText(value.text)].filter(Boolean);
    if (typeof value.name === "string") return [normalizeText(value.name)].filter(Boolean);
    if (Array.isArray(value.itemListElement)) return flattenStructuredText(value.itemListElement);
    return [];
  }

  function findRecipe(value) {
    if (!value) return null;
    if (Array.isArray(value)) {
      for (const item of value) {
        const found = findRecipe(item);
        if (found) return found;
      }
      return null;
    }
    if (typeof value !== "object") return null;
    if (typeIsRecipe(value["@type"])) return value;
    if (Array.isArray(value["@graph"])) return findRecipe(value["@graph"]);
    return null;
  }

  function readStructuredRecipe() {
    for (const script of document.querySelectorAll('script[type="application/ld+json"]')) {
      try {
        const found = findRecipe(JSON.parse(script.textContent || ""));
        if (found) return found;
      } catch {
        // A malformed JSON-LD block should not prevent checking the remaining page.
      }
    }
    return null;
  }

  function readListFromPage(selectors) {
    const lines = [];
    for (const selector of selectors) {
      for (const element of document.querySelectorAll(selector)) {
        const text = normalizeText(element.innerText || element.textContent);
        if (text && !lines.includes(text)) lines.push(text);
      }
      if (lines.length) break;
    }
    return lines.slice(0, 120);
  }

  function numberOfServings(value) {
    const match = normalizeText(value).match(/\d+/);
    if (!match) return 4;
    const count = Number.parseInt(match[0], 10);
    return count > 0 && count <= 10000 ? count : 4;
  }

  function extractRecipe() {
    const structured = readStructuredRecipe();
    const name = normalizeText(structured?.name)
      || normalizeText(document.querySelector('meta[property="og:title"]')?.content)
      || normalizeText(document.title)
      || "Recipe from webpage";

    const ingredients = Array.isArray(structured?.recipeIngredient)
      ? structured.recipeIngredient.map(normalizeText).filter(Boolean)
      : readListFromPage(['[itemprop="recipeIngredient"]', ".recipe-ingredient", ".ingredient"]);

    const instructions = structured?.recipeInstructions
      ? flattenStructuredText(structured.recipeInstructions)
      : readListFromPage(['[itemprop="recipeInstructions"]', ".recipe-instruction", ".recipe-directions li"]);

    const keywords = Array.isArray(structured?.keywords)
      ? structured.keywords
      : typeof structured?.keywords === "string"
        ? structured.keywords.split(",")
        : [];

    return {
      name,
      description: normalizeText(structured?.description)
        || normalizeText(document.querySelector('meta[name="description"]')?.content),
      category: normalizeText(structured?.recipeCategory) || "Other",
      tags: keywords.map(normalizeText).filter(Boolean),
      servings: numberOfServings(structured?.recipeYield),
      ingredients,
      instructions,
      sourceUrl: location.href
    };
  }

  async function pollForCapture() {
    if (busy || document.visibilityState !== "visible") return;

    try {
      const result = await chrome.runtime.sendMessage({ action: "poll" });
      const requestId = result?.requestId;
      if (!requestId) return;
      busy = true;
      const saved = await chrome.runtime.sendMessage({
        action: "captured",
        requestId,
        recipe: extractRecipe()
      });
      if (!saved?.ok && !saved?.alreadyHandled) {
        console.warn("Barb's Recipe Book could not save this webpage recipe.", saved?.error);
      }
    } catch {
      // The desktop app or extension may be unavailable; retry without disrupting the webpage.
    } finally {
      busy = false;
    }
  }

  pollForCapture();
  window.setInterval(pollForCapture, POLL_MS);
})();
