# Barb's Recipe Book

A private, offline recipe library for Windows, with a Chrome/Edge companion that sends a recipe from the active webpage to the desktop app when Barb clicks its floating camera button.

## What it does

- Organizes recipes with editable categories, a broad starter category list, and tags.
- Searches names, descriptions, categories, tags, ingredients, directions, notes, and source URLs.
- Saves custom recipes and webpage captures privately to `%LOCALAPPDATA%\BarbsRecipeBook\recipes.json`, outside the OneDrive project folder.
- Keeps adjusted versions as separate recipes linked to their original; scales recognizable ingredient quantities and leaves the source recipe unchanged.
- Favorites (♥) and 1–5 star ratings, an "I made this today" button, and a Favorites view.
- Warns before saving a recipe you already have (matched by web address or name).
- Deleting moves a recipe to **Trash** (kept 30 days) with an **Undo delete** button; restore or delete forever from the Trash view.
- Ingredient scaling understands ½, 1 1/2, 1-2 ranges and writes tidy fractions. A **Units** switch shows any recipe in metric or US units (and °F/°C) without changing the saved recipe.
- **Grocery list**: tick recipes, matching ingredients are added together; copy, print or save it.
- **Meal planner**: plan breakfast/lunch/dinner/snacks for each day of the week, then make that week's grocery list.
- **What can I make?**: list what you have and see which recipes you can cook, and what is missing.
- Exports and restores JSON backups. Restore first writes a timestamped safety backup.
- Runs without an internet connection for the saved library. Chrome is needed only to capture a webpage.

Web capture reads recipe data marked up on a webpage (such as Schema.org Recipe JSON-LD) and common ingredient/instruction elements. It does not take a screenshot. Sites without structured recipe information may import only a title and source URL; imported recipes are marked **Needs review** and can be completed in the editor.

## Run from source

Install the .NET 8 SDK, then from the project folder:

```powershell
dotnet run --project .\RecipeTool.App\RecipeTool.App.csproj
dotnet run --project .\RecipeTool.Tests\RecipeTool.Tests.csproj
```

## Portable Windows release

Publish a self-contained, single-file Windows executable:

```powershell
dotnet publish .\RecipeTool.App\RecipeTool.App.csproj -p:PublishProfile=PortableWinX64
```

The publish folder contains `BarbsRecipeBook.exe` and a `ChromeExtension` folder. Keep them together to run the portable app and its Chrome capture companion on Windows; .NET does not need to be installed. The recipe library stays in the current computer's local app-data folder, not beside the executable. Use **Export backup** and **Restore backup** to move recipes deliberately between computers.

Open `BarbsRecipeBook.exe` from the **publish** folder, or extract the portable ZIP release first. Do not launch an `.exe` from `bin\Release\net8.0-windows` or a `.dll` from a build folder: those are development outputs, not the packaged portable release.

## Install the Chrome or Edge capture companion (the easy way)

Click **Install browser extension** at the bottom of the app. The wizard copies the extensions address for you: open the browser, click its address bar, press Ctrl+V and Enter. You only set this up once; the browser keeps the extension after restarts. A picture-guided wizard walks through every click, for Chrome or Edge (choose "I use Microsoft Edge instead" on the first step), and shows **Connected** when it works.

Manual steps:

1. Start `BarbsRecipeBook.exe`.
2. In Chrome visit `chrome://extensions` (Edge: `edge://extensions`) and turn on **Developer mode**.
3. Choose **Load unpacked** and select the `Barbs Recipe Book Extension` folder in your Documents (the app creates it when you click **Install browser extension**).
4. Open a recipe webpage. While the desktop app is running, a small camera button appears above other windows. Drag it to move it; click it to capture the active Chrome page.
5. Review the imported recipe in the app. The original webpage URL is saved with it.

The companion checks for the app only from visible HTTP/HTTPS tabs. Its extension service worker uses the declared localhost host permission to communicate with the app; the app's bridge listens on loopback (`127.0.0.1`) only. Chrome does not permit an unpacked extension to run on browser-internal pages such as `chrome://settings`.
