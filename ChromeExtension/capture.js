(() => {
  const POLL_MS = 1800;
  let busy = false;

  const decoder = document.createElement("textarea");

  function normalizeText(value) {
    let text = String(value ?? "");
    if (/[<&]/.test(text)) {
      text = text.replace(/<br\s*\/?>/gi, " ").replace(/<[^>]+>/g, " ");
      decoder.innerHTML = text;
      text = decoder.value;
    }
    return text.replace(/[\s\u00a0]+/g, " ").trim();
  }

  function typeIsRecipe(value) {
    const types = Array.isArray(value) ? value : [value];
    return types.some(type => /(^|[/#:])recipe$/i.test(String(type)));
  }

  function flattenStructuredText(value) {
    if (typeof value === "string") {
      // Some sites ship all steps in one string separated by line breaks.
      return value.split(/\r?\n+|<br\s*\/?>/i).map(normalizeText).filter(Boolean);
    }
    if (Array.isArray(value)) return value.flatMap(flattenStructuredText);
    if (!value || typeof value !== "object") return [];
    if (Array.isArray(value.itemListElement)) return flattenStructuredText(value.itemListElement);
    if (typeof value.text === "string") return flattenStructuredText(value.text);
    if (typeof value.name === "string") return [normalizeText(value.name)].filter(Boolean);
    return [];
  }

  function firstText(value) {
    if (Array.isArray(value)) return firstText(value.find(item => firstText(item)));
    if (value && typeof value === "object") return normalizeText(value.name || value.text || value["@value"]);
    return normalizeText(value);
  }

  function findRecipe(value, depth = 0) {
    if (!value || depth > 8) return null;
    if (Array.isArray(value)) {
      for (const item of value) {
        const found = findRecipe(item, depth + 1);
        if (found) return found;
      }
      return null;
    }
    if (typeof value !== "object") return null;
    if (typeIsRecipe(value["@type"])) return value;
    for (const key of ["@graph", "mainEntity", "mainEntityOfPage", "hasPart", "about", "itemListElement", "item"]) {
      const found = findRecipe(value[key], depth + 1);
      if (found) return found;
    }
    return null;
  }

  function readStructuredRecipe() {
    for (const script of document.querySelectorAll('script[type*="ld+json" i]')) {
      let text = script.textContent || "";
      text = text.replace(/^\s*<!--/, "").replace(/-->\s*$/, "").replace(/^\s*\/\*<!\[CDATA\[\*\//, "").replace(/\/\*\]\]>\*\/\s*$/, "");
      try {
        const found = findRecipe(JSON.parse(text));
        if (found) return found;
      } catch {
        // A malformed JSON-LD block should not prevent checking the remaining page.
      }
    }
    return null;
  }

  function elementLines(element) {
    const items = element.matches("li") ? [element] : [...element.querySelectorAll(":scope li")];
    if (items.length) return items.map(item => normalizeText(item.innerText || item.textContent)).filter(Boolean);
    // No list markup: split the block on line and paragraph breaks.
    const html = element.innerHTML.replace(/<\/(p|div|h[1-6])>|<br\s*\/?>/gi, "\n");
    return html.split(/\n+/).map(normalizeText).filter(Boolean);
  }

  function readListFromPage(selectors) {
    for (const selector of selectors) {
      let elements;
      try {
        elements = [...document.querySelectorAll(selector)];
      } catch {
        continue;
      }
      // Skip containers nested inside another match to avoid duplicates.
      elements = elements.filter(element => !elements.some(other => other !== element && other.contains(element)));
      const lines = [];
      for (const element of elements) {
        for (const text of elementLines(element)) {
          if (!lines.includes(text)) lines.push(text);
        }
      }
      if (lines.length) return lines.slice(0, 200);
    }
    return [];
  }

  const INGREDIENT_SELECTORS = [
    ".wprm-recipe-ingredient",
    ".jetpack-recipe-ingredient",
    ".tasty-recipes-ingredients li",
    ".tasty-recipes-ingredients-body li",
    ".mv-create-ingredients li",
    ".wp-block-recipe-card-ingredients li",
    ".wpzoom-recipe-card-ingredients li",
    ".recipe-card-ingredients li",
    ".cooked-recipe-ingredients .cooked-ing-name, .cooked-single-ingredient",
    ".ingredient-list li",
    ".ingredients-list li",
    ".ingredients__list li",
    ".recipe-ingredients li",
    ".recipe__ingredients li",
    ".recipe-ingredient",
    ".ERSIngredients li",
    ".zlrecipe-container-border [itemprop=ingredients]",
    '[itemprop="recipeIngredient"]',
    '[itemprop="ingredients"]',
    '[data-ingredient]',
    '[class*="ingredients" i] li',
    ".ingredient",
    ".wprm-fallback-recipe-equipment"
  ];

  const INSTRUCTION_SELECTORS = [
    ".wprm-recipe-instruction-text",
    ".jetpack-recipe-directions",
    ".wprm-recipe-instruction",
    ".tasty-recipes-instructions li",
    ".tasty-recipes-instructions-body li",
    ".mv-create-instructions li",
    ".wp-block-recipe-card-directions li",
    ".wpzoom-recipe-card-directions li",
    ".recipe-card-directions li",
    ".cooked-recipe-directions .cooked-dir-content",
    ".instructions-list li",
    ".recipe-directions li",
    ".recipe-directions__list li",
    ".recipe-instructions li",
    ".recipe__instructions li",
    ".recipe-method li",
    ".ERSInstructions li",
    ".zlrecipe-container-border [itemprop=recipeInstructions]",
    '[itemprop="recipeInstructions"]',
    '[class*="instructions" i] li',
    '[class*="directions" i] li',
    ".recipe-instruction",
    ".wprm-fallback-recipe-instructions"
  ];

  const INGREDIENT_HEADING = /^(ingredients?|what you.?ll need|you.?ll need|shopping list)\b[\s:]*$/i;
  const INSTRUCTION_HEADING = /^(instructions?|directions?|method|preparation|steps|how to make( it)?|procedure)\b[\s:]*$/i;
  const STOP_HEADING = /^(notes?|nutrition|equipment|tips?|video|related|comments?|reviews?|you may also like|share|print)\b/i;

  function headingLevel(element) {
    const match = /^H([1-6])$/.exec(element.tagName);
    if (match) return Number(match[1]);
    // Bold paragraphs commonly stand in for headings.
    if (/^(P|DIV|SPAN|STRONG|B)$/.test(element.tagName)) {
      const text = normalizeText(element.textContent);
      if (text.length < 40 && element.children.length <= 1 && (element.tagName === "STRONG" || element.tagName === "B" || element.querySelector(":scope > strong, :scope > b"))) {
        return 7;
      }
    }
    return 0;
  }

  // Finds a heading like "Ingredients" and gathers the list items that follow it.
  function readListByHeading(pattern) {
    const candidates = document.querySelectorAll("h1, h2, h3, h4, h5, h6, strong, b, p, div, span, dt, summary");
    for (const heading of candidates) {
      if (heading.children.length > 1) continue;
      const text = normalizeText(heading.textContent);
      if (text.length > 40 || !pattern.test(text)) continue;
      const level = headingLevel(heading) || 7;

      const lines = [];
      let node = heading;
      for (let climb = 0; climb < 4 && !lines.length; climb++) {
        for (let sibling = node.nextElementSibling; sibling; sibling = sibling.nextElementSibling) {
          const siblingLevel = headingLevel(sibling);
          if (siblingLevel && siblingLevel <= level) {
            const siblingText = normalizeText(sibling.textContent);
            if (!/^for\b/i.test(siblingText) && (INGREDIENT_HEADING.test(siblingText) || INSTRUCTION_HEADING.test(siblingText) || STOP_HEADING.test(siblingText) || siblingLevel < level)) break;
          }
          if (/^(UL|OL)$/.test(sibling.tagName) || sibling.querySelector("ul, ol")) {
            for (const item of sibling.matches("ul, ol") ? sibling.querySelectorAll(":scope > li") : sibling.querySelectorAll("li")) {
              const line = normalizeText(item.innerText || item.textContent);
              if (line && !lines.includes(line)) lines.push(line);
            }
          } else if (sibling.tagName === "P" && lines.length === 0 && !siblingLevel) {
            const line = normalizeText(sibling.innerText || sibling.textContent);
            if (line) lines.push(...sibling.innerText.split(/\n+/).map(normalizeText).filter(Boolean));
          }
        }
        node = node.parentElement;
        if (!node || node === document.body) break;
      }
      if (lines.length >= 2) return lines.slice(0, 200);
    }
    return [];
  }

  function readMicrodata() {
    const root = document.querySelector('[itemtype*="schema.org/Recipe" i]');
    if (!root) return null;
    const prop = name => normalizeText(root.querySelector(`[itemprop="${name}"]`)?.getAttribute("content")
      || root.querySelector(`[itemprop="${name}"]`)?.textContent);
    return { name: prop("name"), description: prop("description"), recipeYield: prop("recipeYield") };
  }

  function numberOfServings(value) {
    const match = normalizeText(Array.isArray(value) ? value.join(" ") : value).match(/\d+/);
    if (!match) return 4;
    const count = Number.parseInt(match[0], 10);
    return count > 0 && count <= 10000 ? count : 4;
  }

  function cleanTitle(title) {
    return normalizeText(title).replace(/\s+[|\u2013\u2014-]\s+[^|\u2013\u2014-]+$/, "") || normalizeText(title);
  }

  function extractRecipe() {
    const structured = readStructuredRecipe() || readMicrodata();

    const wprmTitle = document.querySelector(".wprm-recipe-name, .tasty-recipes-title, .mv-create-title, .wprm-fallback-recipe-name");
    const name = firstText(structured?.name)
      || normalizeText(wprmTitle?.textContent)
      || cleanTitle(document.querySelector('meta[property="og:title"]')?.content)
      || cleanTitle(document.title)
      || "Recipe from webpage";

    let ingredients = flattenIngredientList(structured?.recipeIngredient ?? structured?.ingredients);
    if (!ingredients.length) {
      ingredients = readListFromPage(INGREDIENT_SELECTORS);
    }
    if (!ingredients.length) ingredients = readListByHeading(INGREDIENT_HEADING);

    let instructions = flattenStructuredText(structured?.recipeInstructions);
    if (!instructions.length) {
      instructions = readListFromPage(INSTRUCTION_SELECTORS);
    }
    if (!instructions.length) instructions = readListByHeading(INSTRUCTION_HEADING);

    const rawKeywords = structured?.keywords;
    const keywords = Array.isArray(rawKeywords)
      ? rawKeywords
      : typeof rawKeywords === "string"
        ? rawKeywords.split(",")
        : [];

    return {
      name,
      description: normalizeText(structured?.description)
        || normalizeText(document.querySelector(".wprm-fallback-recipe-summary, .wprm-recipe-summary")?.textContent)
        || normalizeText(document.querySelector('meta[name="description"]')?.content)
        || normalizeText(document.querySelector('meta[property="og:description"]')?.content),
      category: firstText(structured?.recipeCategory) || "Other",
      tags: keywords.map(normalizeText).filter(Boolean),
      servings: numberOfServings(structured?.recipeYield),
      ingredients,
      instructions,
      sourceUrl: location.href
    };
  }

  function flattenIngredientList(value) {
    if (typeof value === "string") return value.split(/\r?\n+/).map(normalizeText).filter(Boolean);
    return flattenStructuredText(value);
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


