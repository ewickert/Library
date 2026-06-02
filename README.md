# Magic: The Gathering Library

A cross-platform desktop app for managing your Magic: The Gathering card collection, building decks, organising binders, and planning card purchases.

---

## Installation

Download the latest release for your platform from the [Releases page](https://github.com/ewickert/Library/releases).

| File | Platform |
|---|---|
| `MtgLibrary-osx-arm64.zip` | macOS (Apple Silicon) |
| `MtgLibrary-osx-x64.zip` | macOS (Intel) |
| `MtgLibrary-win-x64.zip` | Windows 10/11 (64-bit) |

**macOS:** Unzip, then double-click `MtgLibrary.app`. On first launch right-click → **Open** to bypass Gatekeeper (the binary is not notarised).

**Windows:** Unzip and run `Library.exe`. No installer required.

Your collection database is stored in the same folder as the executable as `library.db`.

---

## Tabs

### Collection

Your full physical card collection.

**Adding cards**
- Click **+ Add Card** to open the card entry form. Fill in name, set, collector number, condition, language, quantity, foil, etc.
- Click **Import CSV** to bulk-import from a ManaBox-compatible CSV export.

**Browsing**
- Switch between **☰ List** and **⊞ Grid** views with the toggle button.
- Filter by set using the dropdown next to the search box.
- Hover over any card row or grid tile for a full-size card image preview.
- Ctrl+Scroll (or ⌘+Scroll on Mac) zooms the grid view in and out.

**Searching**
- Type plain text in the search box to filter by name, set code, or set name.
- Click **?** to open the search syntax guide, or see [Search Syntax](#search-syntax) below.
- Enable **Search Scryfall** to search Scryfall directly for cards not in your collection. Results are shown alongside a **+ Want It** button to add them to your shopping list.

**Metadata**
- Click **↻ Fill Metadata** to fetch mana cost, type line, and colour identity from Scryfall for any cards that are missing it.

---

### Decks

Build and manage decks. A collection pane on the right lets you add cards from your collection.

**Creating a deck**
- Click **+ New Deck**, fill in name, format, and description, then click **Create**.

**Adding cards to a deck**
- From the **collection pane** on the right, click **+** next to a card in list view, or the **+** button on a grid tile.
- **Drag** a card from the collection pane and drop it onto the deck area.
- Use **Search Scryfall** in the collection pane to find cards you don't own yet. Drag or click **+ Want It** to add them to the deck *and* the shopping list simultaneously (they appear with a **$** watermark to indicate they're not yet owned).

**Commander format**
- Set a deck's format to **Commander** or **EDH** to enable commander-specific features.
- Right-click a card in the list or click **⚔** to designate a commander.
- The collection pane automatically filters to cards legal in the commander's colour identity.
- Click **Clear** to remove the commander assignment.

**Views**
- Toggle between **☰ List** and **⊞ Grid** for both the deck and the collection pane independently.
- Ctrl/⌘+Scroll to zoom the grid views.
- Hover any card for a full-size preview tooltip.

**Removing cards**
- In list view, click **✕** on a row.

---

### Binders

Organise cards into named binders (e.g. by set, trade binder, display binder).

- Click **+ New Binder** to create a binder.
- Drag cards from the card list into a binder slot, or use the list view to manage contents directly.
- Toggle **⊞ Grid / ☰ List** view.
- Delete a binder with the **Delete Binder** button.

---

### 🛒 Shopping

Your wishlist of cards to acquire.

**How cards end up here**
- Click **+ Want It** on any Scryfall search result in the Collection or Decks tab.
- Drag a Scryfall result into a deck — it is added to both the deck and the shopping list automatically.

**Using the shopping list**
- Each entry shows the card image, name, type, mana cost, set, rarity, and which deck(s) it was added for.
- Use the text filter to search by name, type, or set.
- Use the **deck dropdown** to filter the list to cards wanted for a specific deck.
- Click **Add to Deck** to add the card to whichever deck is currently selected in the Decks tab.
- Click **Remove** to delete the entry from the shopping list.

> The Shopping list is refreshed every time you switch to the tab, so newly added cards always appear.

---

## Search Syntax

The search box supports both plain-text and Scryfall-style keyword queries. Multiple filters are ANDed together. Prefix any token with `-` to negate it.

| Keyword | Example | Description |
|---|---|---|
| `t:` `type:` | `t:creature` `t:legendary` | Type line |
| `c:` `color:` | `c:wu` `c:m` `c:c` | Color identity (m = multicolour, c = colorless) |
| `id:` `identity:` | `id:uw` | Cards legal in a commander deck of that identity |
| `r:` `rarity:` | `r:r` `r:m` | Rarity: c / u / r / m |
| `s:` `set:` `e:` | `s:mkm` `e:dom` | Set code or set name |
| `cmc:` `mv:` | `cmc>=5` `mv=3` | Mana value with operators |
| `foil:` | `foil:yes` `-foil:yes` | Foil flag |
| `lang:` | `lang:en` `lang:de` | Language |
| `condition:` | `condition:nm` | Condition (NM, LP, MP, HP, DMG) |
| `q:` `qty:` | `q>=4` `qty=1` | Quantity |

**Operators:** `:` `=` `!=` `<` `<=` `>` `>=`  
Colon (`:`) means "contains" for text and `>=` for numbers.

**Examples:**
```
lightning bolt               → all cards with "lightning bolt" in the name
t:instant c:u r:r            → rare blue instants
cmc<=2 t:creature -foil:yes  → non-foil creatures with mana value 2 or less
s:mkm t:legendary            → legendary cards from Murders at Karlov Manor
```

---

## Building from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/ewickert/Library.git
cd Library
dotnet run
```

### Run by platform

The app now uses a shared Avalonia UI across desktop, Android, and iOS targets.

```bash
# Desktop (macOS / Windows / Linux)
dotnet run -f net10.0

# Android (requires Android workload + emulator/device)
dotnet run -f net10.0-android

# iOS (requires iOS workload + simulator/device on macOS)
dotnet run -f net10.0-ios
```

### Debug scripts for simulators/emulators

Use these scripts to boot a matching simulator/emulator and start a debug run:

```bash
./scripts/debug-android-phone.sh
./scripts/debug-android-tablet.sh
./scripts/debug-ios-iphone.sh
./scripts/debug-ios-ipad.sh
```

Optional overrides (if you want a specific named simulator/AVD):

```bash
ANDROID_PHONE_AVD="Pixel_8_API_35" ./scripts/debug-android-phone.sh
ANDROID_TABLET_AVD="Pixel_Tablet_API_35" ./scripts/debug-android-tablet.sh
IOS_IPHONE_SIM="iPhone 16 Pro" ./scripts/debug-ios-iphone.sh
IOS_IPAD_SIM="iPad Pro (13-inch)" ./scripts/debug-ios-ipad.sh

# Disable iOS auto-fallback to any simulator when no iPhone/iPad match exists
IOS_FALLBACK_ANY_SIM=0 ./scripts/debug-ios-ipad.sh
```

### Publishing releases

```bash
# Build self-contained binaries for all platforms + zip them
./publish.sh

# Create a GitHub release and upload the artifacts
./do-release.sh v1.0.0
```

See `publish.sh --help` / `do-release.sh` for additional options (`--skip-build`, `--draft`, `--prerelease`).
