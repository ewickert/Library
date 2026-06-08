using Library.Models;
using Microsoft.Data.Sqlite;

namespace Library.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MagicLibrary");
        Directory.CreateDirectory(appData);
        var dbPath = Path.Combine(appData, "library.db");
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    private void InitializeDatabase()
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Cards (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                SetCode TEXT NOT NULL DEFAULT '',
                SetName TEXT NOT NULL DEFAULT '',
                CollectorNumber TEXT NOT NULL DEFAULT '',
                Foil INTEGER NOT NULL DEFAULT 0,
                Rarity TEXT NOT NULL DEFAULT '',
                Quantity INTEGER NOT NULL DEFAULT 1,
                ManaBoxId INTEGER,
                ScryfallId TEXT,
                PurchasePrice REAL,
                Misprint INTEGER NOT NULL DEFAULT 0,
                Altered INTEGER NOT NULL DEFAULT 0,
                Condition TEXT NOT NULL DEFAULT 'NM',
                Language TEXT NOT NULL DEFAULT 'en',
                PurchasePriceCurrency TEXT,
                Added TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS Decks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT,
                Format TEXT,
                Created TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS DeckCards (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeckId INTEGER NOT NULL REFERENCES Decks(Id) ON DELETE CASCADE,
                CardId INTEGER NOT NULL REFERENCES Cards(Id) ON DELETE CASCADE,
                Quantity INTEGER NOT NULL DEFAULT 1,
                IsSideboard INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Binders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT,
                Created TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS BinderCards (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BinderId INTEGER NOT NULL REFERENCES Binders(Id) ON DELETE CASCADE,
                CardId INTEGER NOT NULL REFERENCES Cards(Id) ON DELETE CASCADE,
                Quantity INTEGER NOT NULL DEFAULT 1,
                SlotIndex INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS ShoppingList (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ScryfallId TEXT NOT NULL,
                Name TEXT NOT NULL,
                SetCode TEXT NOT NULL DEFAULT '',
                SetName TEXT NOT NULL DEFAULT '',
                CollectorNumber TEXT NOT NULL DEFAULT '',
                ManaCost TEXT NOT NULL DEFAULT '',
                TypeLine TEXT NOT NULL DEFAULT '',
                ColorIdentity TEXT NOT NULL DEFAULT '',
                Rarity TEXT NOT NULL DEFAULT '',
                Added TEXT NOT NULL DEFAULT (datetime('now')),
                PlaceholderCardId INTEGER
            );

            CREATE TABLE IF NOT EXISTS Games (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlayedAt TEXT NOT NULL DEFAULT (datetime('now')),
                TurnEnded INTEGER,
                Notes TEXT
            );

            CREATE TABLE IF NOT EXISTS GamePlayers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GameId INTEGER NOT NULL REFERENCES Games(Id) ON DELETE CASCADE,
                PlayerName TEXT NOT NULL DEFAULT '',
                IsMe INTEGER NOT NULL DEFAULT 0,
                DeckId INTEGER REFERENCES Decks(Id) ON DELETE SET NULL,
                DeckName TEXT,
                FinishPosition INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS DeckVersions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeckId INTEGER NOT NULL REFERENCES Decks(Id) ON DELETE CASCADE,
                VersionNumber INTEGER NOT NULL,
                Label TEXT,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                IsAuto INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS DeckVersionCards (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                VersionId INTEGER NOT NULL REFERENCES DeckVersions(Id) ON DELETE CASCADE,
                CardId INTEGER NOT NULL REFERENCES Cards(Id) ON DELETE CASCADE,
                CardName TEXT NOT NULL DEFAULT '',
                SetCode TEXT NOT NULL DEFAULT '',
                CollectorNumber TEXT NOT NULL DEFAULT '',
                Quantity INTEGER NOT NULL DEFAULT 1,
                IsSideboard INTEGER NOT NULL DEFAULT 0,
                IsCommander INTEGER NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();

        // Safe migrations — ignore errors if column already exists
        RunMigration(conn, "ALTER TABLE Cards ADD COLUMN ColorIdentity TEXT");
        RunMigration(conn, "ALTER TABLE Cards ADD COLUMN ManaCost TEXT");
        RunMigration(conn, "ALTER TABLE Cards ADD COLUMN TypeLine TEXT");
        RunMigration(conn, "ALTER TABLE Decks ADD COLUMN CommanderColorIdentity TEXT");
        RunMigration(conn, "ALTER TABLE DeckCards ADD COLUMN IsCommander INTEGER NOT NULL DEFAULT 0");
        RunMigration(conn, "ALTER TABLE Cards ADD COLUMN IsPlaceholder INTEGER NOT NULL DEFAULT 0");
        RunMigration(conn, "ALTER TABLE Cards ADD COLUMN BaselineMarketPrice REAL");
        RunMigration(conn, "ALTER TABLE Cards ADD COLUMN BaselineMarketPriceFetchedAt TEXT");
        RunMigration(conn, "ALTER TABLE Cards ADD COLUMN CurrentMarketPrice REAL");
        RunMigration(conn, "ALTER TABLE Cards ADD COLUMN CurrentMarketPriceFetchedAt TEXT");
        RunMigration(conn, "ALTER TABLE GamePlayers ADD COLUMN DeckVersionId INTEGER REFERENCES DeckVersions(Id) ON DELETE SET NULL");
        RunMigration(conn, """
            CREATE TABLE IF NOT EXISTS DeckShoppingItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeckId INTEGER NOT NULL REFERENCES Decks(Id) ON DELETE CASCADE,
                ShoppingListId INTEGER NOT NULL REFERENCES ShoppingList(Id) ON DELETE CASCADE,
                UNIQUE(DeckId, ShoppingListId)
            )
            """);
    }

    private static void RunMigration(SqliteConnection conn, string sql)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        catch { /* column already exists */ }
    }

    // --- Cards ---

    public List<Card> GetAllCards()
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Cards WHERE IsPlaceholder=0 OR IsPlaceholder IS NULL ORDER BY Name";
        using var reader = cmd.ExecuteReader();
        var cards = new List<Card>();
        while (reader.Read()) cards.Add(ReadCard(reader));
        return cards;
    }

    public Card? GetCard(int id)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Cards WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadCard(reader) : null;
    }

    /// <summary>Returns the first owned non-placeholder card with this name (case-insensitive), if any.</summary>
    public Card? GetCardByName(string name)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM Cards
            WHERE Name = $name COLLATE NOCASE
              AND (IsPlaceholder = 0 OR IsPlaceholder IS NULL)
            ORDER BY Id
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$name", name);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadCard(reader) : null;
    }

    public Card? GetCardBySetAndCollector(string name, string setCode, string? collectorNumber)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (!string.IsNullOrWhiteSpace(collectorNumber))
        {
            cmd.CommandText = """
                SELECT * FROM Cards
                WHERE Name = $name COLLATE NOCASE
                  AND SetCode = $set COLLATE NOCASE
                  AND CollectorNumber = $cn COLLATE NOCASE
                  AND (IsPlaceholder = 0 OR IsPlaceholder IS NULL)
                ORDER BY Id LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$set", setCode);
            cmd.Parameters.AddWithValue("$cn", collectorNumber);
        }
        else
        {
            cmd.CommandText = """
                SELECT * FROM Cards
                WHERE Name = $name COLLATE NOCASE
                  AND SetCode = $set COLLATE NOCASE
                  AND (IsPlaceholder = 0 OR IsPlaceholder IS NULL)
                ORDER BY Id LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$set", setCode);
        }
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadCard(reader) : null;
    }

    /// <summary>Returns the first owned non-placeholder card with this Scryfall ID, if any.</summary>
    public Card? GetOwnedCardByScryfallId(string scryfallId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM Cards
            WHERE ScryfallId = $sid
              AND Quantity > 0
              AND (IsPlaceholder = 0 OR IsPlaceholder IS NULL)
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$sid", scryfallId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadCard(reader) : null;
    }

    public bool IsInCollectionByScryfallId(string scryfallId) =>
        GetOwnedCardByScryfallId(scryfallId) != null;

    public int AddCard(Card card)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Cards (Name, SetCode, SetName, CollectorNumber, Foil, Rarity,
                Quantity, ManaBoxId, ScryfallId, PurchasePrice, Misprint, Altered,
                Condition, Language, PurchasePriceCurrency, Added, ColorIdentity, ManaCost, TypeLine)
            VALUES ($name,$setCode,$setName,$collNum,$foil,$rarity,
                $qty,$manaBoxId,$scryfallId,$price,$misprint,$altered,
                $cond,$lang,$currency,$added,$colorIdentity,$manaCost,$typeLine);
            SELECT last_insert_rowid();
            """;
        BindCardParams(cmd, card);
        card.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return card.Id;
    }

    public void UpdateCard(Card card)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Cards SET Name=$name, SetCode=$setCode, SetName=$setName,
                CollectorNumber=$collNum, Foil=$foil, Rarity=$rarity, Quantity=$qty,
                ManaBoxId=$manaBoxId, ScryfallId=$scryfallId, PurchasePrice=$price,
                Misprint=$misprint, Altered=$altered, Condition=$cond, Language=$lang,
                PurchasePriceCurrency=$currency, Added=$added, ColorIdentity=$colorIdentity,
                ManaCost=$manaCost, TypeLine=$typeLine
            WHERE Id=$id
            """;
        BindCardParams(cmd, card);
        cmd.Parameters.AddWithValue("$id", card.Id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Updates only the purchase price and currency for an existing card.</summary>
    public void UpdateCardPurchasePrice(int id, decimal? price, string? currency)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Cards SET PurchasePrice=$price, PurchasePriceCurrency=$currency WHERE Id=$id";
        cmd.Parameters.AddWithValue("$price",    (object?)price    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$currency", (object?)currency ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id",       id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Updates only the three Scryfall-derived metadata columns for an existing card.</summary>
    public void UpdateCardMetadata(int id, string? colorIdentity, string? manaCost, string? typeLine)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Cards SET ColorIdentity=$ci, ManaCost=$mc, TypeLine=$tl WHERE Id=$id
            """;
        cmd.Parameters.AddWithValue("$ci",  (object?)colorIdentity ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mc",  (object?)manaCost      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tl",  (object?)typeLine      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id",  id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns all cards missing a baseline price that have a Scryfall ID (excluding placeholders).</summary>
    public List<Card> GetCardsNeedingBaselinePrice()
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM Cards
            WHERE (IsPlaceholder = 0 OR IsPlaceholder IS NULL)
              AND ScryfallId IS NOT NULL
              AND BaselineMarketPrice IS NULL
            ORDER BY Name
            """;
        using var reader = cmd.ExecuteReader();
        var cards = new List<Card>();
        while (reader.Read()) cards.Add(ReadCard(reader));
        return cards;
    }

    /// <summary>
    /// Returns cards whose current price is missing or was fetched more than 24 hours ago.
    /// Excludes placeholder cards (they get their prices via the shopping list flow).
    /// </summary>
    public List<Card> GetCardsForPriceRefresh()
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        var cutoff = DateTime.UtcNow.AddHours(-24).ToString("o");
        cmd.CommandText = """
            SELECT * FROM Cards
            WHERE (IsPlaceholder = 0 OR IsPlaceholder IS NULL)
              AND ScryfallId IS NOT NULL
              AND (CurrentMarketPriceFetchedAt IS NULL OR CurrentMarketPriceFetchedAt < $cutoff)
            ORDER BY Name
            """;
        cmd.Parameters.AddWithValue("$cutoff", cutoff);
        using var reader = cmd.ExecuteReader();
        var cards = new List<Card>();
        while (reader.Read()) cards.Add(ReadCard(reader));
        return cards;
    }

    /// <summary>Sets the baseline price (once ever) and always updates the current price.</summary>
    public void UpdateCardPrices(int id, decimal price, bool setBaseline)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        var now = DateTime.UtcNow.ToString("o");
        if (setBaseline)
        {
            cmd.CommandText = """
                UPDATE Cards SET
                    BaselineMarketPrice = $price,
                    BaselineMarketPriceFetchedAt = $now,
                    CurrentMarketPrice = $price,
                    CurrentMarketPriceFetchedAt = $now
                WHERE Id = $id
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE Cards SET
                    CurrentMarketPrice = $price,
                    CurrentMarketPriceFetchedAt = $now
                WHERE Id = $id
                """;
        }
        cmd.Parameters.AddWithValue("$price", price);
        cmd.Parameters.AddWithValue("$now",   now);
        cmd.Parameters.AddWithValue("$id",    id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns all cards that are missing at least one of the three metadata columns.</summary>
    public List<Card> GetCardsNeedingMetadata()
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM Cards
            WHERE ColorIdentity IS NULL OR ManaCost IS NULL OR TypeLine IS NULL OR TypeLine = ''
            ORDER BY Name
            """;
        using var reader = cmd.ExecuteReader();
        var cards = new List<Card>();
        while (reader.Read()) cards.Add(ReadCard(reader));
        return cards;
    }

    public void DeleteCard(int id)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Cards WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static void BindCardParams(SqliteCommand cmd, Card card)
    {
        cmd.Parameters.AddWithValue("$name", card.Name);
        cmd.Parameters.AddWithValue("$setCode", card.SetCode);
        cmd.Parameters.AddWithValue("$setName", card.SetName);
        cmd.Parameters.AddWithValue("$collNum", card.CollectorNumber);
        cmd.Parameters.AddWithValue("$foil", card.Foil ? 1 : 0);
        cmd.Parameters.AddWithValue("$rarity", card.Rarity);
        cmd.Parameters.AddWithValue("$qty", card.Quantity);
        cmd.Parameters.AddWithValue("$manaBoxId", (object?)card.ManaBoxId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$scryfallId", (object?)card.ScryfallId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$price", (object?)card.PurchasePrice ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$misprint", card.Misprint ? 1 : 0);
        cmd.Parameters.AddWithValue("$altered", card.Altered ? 1 : 0);
        cmd.Parameters.AddWithValue("$cond", card.Condition);
        cmd.Parameters.AddWithValue("$lang", card.Language);
        cmd.Parameters.AddWithValue("$currency", (object?)card.PurchasePriceCurrency ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$added", card.Added.ToString("o"));
        cmd.Parameters.AddWithValue("$colorIdentity", (object?)card.ColorIdentity ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$manaCost", (object?)card.ManaCost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$typeLine", (object?)card.TypeLine ?? DBNull.Value);
    }

    private static Card ReadCard(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Name = r.GetString(r.GetOrdinal("Name")),
        SetCode = r.GetString(r.GetOrdinal("SetCode")),
        SetName = r.GetString(r.GetOrdinal("SetName")),
        CollectorNumber = r.GetString(r.GetOrdinal("CollectorNumber")),
        Foil = r.GetInt32(r.GetOrdinal("Foil")) == 1,
        Rarity = r.GetString(r.GetOrdinal("Rarity")),
        Quantity = r.GetInt32(r.GetOrdinal("Quantity")),
        ManaBoxId = r.IsDBNull(r.GetOrdinal("ManaBoxId")) ? null : r.GetInt64(r.GetOrdinal("ManaBoxId")),
        ScryfallId = r.IsDBNull(r.GetOrdinal("ScryfallId")) ? null : r.GetString(r.GetOrdinal("ScryfallId")),
        PurchasePrice = r.IsDBNull(r.GetOrdinal("PurchasePrice")) ? null : r.GetDecimal(r.GetOrdinal("PurchasePrice")),
        Misprint = r.GetInt32(r.GetOrdinal("Misprint")) == 1,
        Altered = r.GetInt32(r.GetOrdinal("Altered")) == 1,
        Condition = r.GetString(r.GetOrdinal("Condition")),
        Language = r.GetString(r.GetOrdinal("Language")),
        PurchasePriceCurrency = r.IsDBNull(r.GetOrdinal("PurchasePriceCurrency")) ? null : r.GetString(r.GetOrdinal("PurchasePriceCurrency")),
        Added = DateTime.Parse(r.GetString(r.GetOrdinal("Added"))),
        ColorIdentity = r.IsDBNull(r.GetOrdinal("ColorIdentity")) ? null : r.GetString(r.GetOrdinal("ColorIdentity")),
        ManaCost = r.IsDBNull(r.GetOrdinal("ManaCost")) ? null : r.GetString(r.GetOrdinal("ManaCost")),
        TypeLine = r.IsDBNull(r.GetOrdinal("TypeLine")) ? null : r.GetString(r.GetOrdinal("TypeLine")),
        IsPlaceholder = r.IsDBNull(r.GetOrdinal("IsPlaceholder")) ? false : r.GetInt32(r.GetOrdinal("IsPlaceholder")) == 1,
        BaselineMarketPrice = r.IsDBNull(r.GetOrdinal("BaselineMarketPrice")) ? null : r.GetDecimal(r.GetOrdinal("BaselineMarketPrice")),
        BaselineMarketPriceFetchedAt = r.IsDBNull(r.GetOrdinal("BaselineMarketPriceFetchedAt")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("BaselineMarketPriceFetchedAt"))),
        CurrentMarketPrice = r.IsDBNull(r.GetOrdinal("CurrentMarketPrice")) ? null : r.GetDecimal(r.GetOrdinal("CurrentMarketPrice")),
        CurrentMarketPriceFetchedAt = r.IsDBNull(r.GetOrdinal("CurrentMarketPriceFetchedAt")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("CurrentMarketPriceFetchedAt")))
    };

    // --- Shopping List ---

    public List<ShoppingItem> GetShoppingList()
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT sl.*,
                   c.CurrentMarketPrice       AS CardCurrentMarketPrice,
                   c.CurrentMarketPriceFetchedAt AS CardCurrentMarketPriceFetchedAt
            FROM ShoppingList sl
            LEFT JOIN Cards c ON c.Id = sl.PlaceholderCardId
            ORDER BY sl.Name
            """;
        using var reader = cmd.ExecuteReader();
        var list = new List<ShoppingItem>();
        while (reader.Read()) list.Add(ReadShoppingItem(reader));
        return list;
    }

    public bool IsOnShoppingList(string scryfallId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ShoppingList WHERE ScryfallId=$id";
        cmd.Parameters.AddWithValue("$id", scryfallId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Returns the Cards.Id of the placeholder row for the given Scryfall ID, or null if none exists.</summary>
    public int? GetPlaceholderCardId(string scryfallId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Cards WHERE ScryfallId=$sid AND IsPlaceholder=1 LIMIT 1";
        cmd.Parameters.AddWithValue("$sid", scryfallId);
        var val = cmd.ExecuteScalar();
        return val != null && val != DBNull.Value ? Convert.ToInt32(val) : null;
    }

    /// <summary>
    /// Adds a card to the shopping list, creating a placeholder Card row (Quantity=0, IsPlaceholder=1)
    /// so it can be referenced by deck and binder tables. Returns the new ShoppingItem.Id.
    /// </summary>
    public int AddToShoppingList(ScryfallCardData data)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        // Check if a placeholder card already exists for this ScryfallId
        int placeholderCardId;
        using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.Transaction = tx;
            checkCmd.CommandText = "SELECT Id FROM Cards WHERE ScryfallId=$sid AND IsPlaceholder=1 LIMIT 1";
            checkCmd.Parameters.AddWithValue("$sid", data.ScryfallId);
            var existing = checkCmd.ExecuteScalar();
            if (existing != null && existing != DBNull.Value)
            {
                placeholderCardId = Convert.ToInt32(existing);
            }
            else
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO Cards (Name, SetCode, SetName, CollectorNumber, Foil, Rarity,
                        Quantity, ScryfallId, Condition, Language, Added,
                        ColorIdentity, ManaCost, TypeLine, IsPlaceholder)
                    VALUES ($name,$setCode,$setName,$collNum,0,$rarity,
                        0,$scryfallId,'NM','en',$added,
                        $colorIdentity,$manaCost,$typeLine,1);
                    SELECT last_insert_rowid();
                    """;
                ins.Parameters.AddWithValue("$name", data.Name);
                ins.Parameters.AddWithValue("$setCode", data.SetCode);
                ins.Parameters.AddWithValue("$setName", data.SetName);
                ins.Parameters.AddWithValue("$collNum", data.CollectorNumber);
                ins.Parameters.AddWithValue("$rarity", data.Rarity);
                ins.Parameters.AddWithValue("$scryfallId", data.ScryfallId);
                ins.Parameters.AddWithValue("$added", DateTime.UtcNow.ToString("o"));
                ins.Parameters.AddWithValue("$colorIdentity", data.ColorIdentity);
                ins.Parameters.AddWithValue("$manaCost", data.ManaCost);
                ins.Parameters.AddWithValue("$typeLine", data.TypeLine);
                placeholderCardId = Convert.ToInt32(ins.ExecuteScalar());
            }
        }

        // Insert into ShoppingList (avoid duplicates by ScryfallId)
        int shoppingId;
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT OR IGNORE INTO ShoppingList (ScryfallId, Name, SetCode, SetName, CollectorNumber,
                    ManaCost, TypeLine, ColorIdentity, Rarity, Added, PlaceholderCardId)
                VALUES ($sid,$name,$setCode,$setName,$collNum,
                    $manaCost,$typeLine,$colorIdentity,$rarity,$added,$placeholderCardId);
                SELECT COALESCE((SELECT Id FROM ShoppingList WHERE ScryfallId=$sid), last_insert_rowid());
                """;
            ins.Parameters.AddWithValue("$sid", data.ScryfallId);
            ins.Parameters.AddWithValue("$name", data.Name);
            ins.Parameters.AddWithValue("$setCode", data.SetCode);
            ins.Parameters.AddWithValue("$setName", data.SetName);
            ins.Parameters.AddWithValue("$collNum", data.CollectorNumber);
            ins.Parameters.AddWithValue("$manaCost", data.ManaCost);
            ins.Parameters.AddWithValue("$typeLine", data.TypeLine);
            ins.Parameters.AddWithValue("$colorIdentity", data.ColorIdentity);
            ins.Parameters.AddWithValue("$rarity", data.Rarity);
            ins.Parameters.AddWithValue("$added", DateTime.UtcNow.ToString("o"));
            ins.Parameters.AddWithValue("$placeholderCardId", placeholderCardId);
            shoppingId = Convert.ToInt32(ins.ExecuteScalar());
        }

        tx.Commit();
        return shoppingId;
    }

    /// <summary>Removes a shopping list item. Also deletes the placeholder Card if no deck/binder references it.</summary>
    public void RemoveFromShoppingList(int shoppingId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        int? placeholderCardId = null;
        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT PlaceholderCardId FROM ShoppingList WHERE Id=$id";
            sel.Parameters.AddWithValue("$id", shoppingId);
            var val = sel.ExecuteScalar();
            if (val != null && val != DBNull.Value) placeholderCardId = Convert.ToInt32(val);
        }

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM ShoppingList WHERE Id=$id";
            del.Parameters.AddWithValue("$id", shoppingId);
            del.ExecuteNonQuery();
        }

        if (placeholderCardId.HasValue)
        {
            using var check = conn.CreateCommand();
            check.Transaction = tx;
            check.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM DeckCards   WHERE CardId=$cid) +
                    (SELECT COUNT(*) FROM BinderCards WHERE CardId=$cid)
                """;
            check.Parameters.AddWithValue("$cid", placeholderCardId.Value);
            var total = Convert.ToInt64(check.ExecuteScalar());
            if (total == 0)
            {
                using var delCard = conn.CreateCommand();
                delCard.Transaction = tx;
                delCard.CommandText = "DELETE FROM Cards WHERE Id=$cid AND IsPlaceholder=1";
                delCard.Parameters.AddWithValue("$cid", placeholderCardId.Value);
                delCard.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    /// <summary>Tags an existing shopping list item as a wanted upgrade for a specific deck.</summary>
    public void TagShoppingItemToDeck(int shoppingListId, int deckId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO DeckShoppingItems (DeckId, ShoppingListId) VALUES ($d,$s)";
        cmd.Parameters.AddWithValue("$d", deckId);
        cmd.Parameters.AddWithValue("$s", shoppingListId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Removes the deck-specific tag from a shopping list item without deleting the global item.</summary>
    public void UntagShoppingItemFromDeck(int shoppingListId, int deckId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DeckShoppingItems WHERE DeckId=$d AND ShoppingListId=$s";
        cmd.Parameters.AddWithValue("$d", deckId);
        cmd.Parameters.AddWithValue("$s", shoppingListId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns all shopping list items tagged as upgrades for a specific deck.</summary>
    public List<ShoppingItem> GetShoppingListForDeck(int deckId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT sl.*,
                   c.CurrentMarketPrice          AS CardCurrentMarketPrice,
                   c.CurrentMarketPriceFetchedAt AS CardCurrentMarketPriceFetchedAt
            FROM ShoppingList sl
            INNER JOIN DeckShoppingItems dsi ON dsi.ShoppingListId = sl.Id
            LEFT  JOIN Cards c ON c.Id = sl.PlaceholderCardId
            WHERE dsi.DeckId = $d
            ORDER BY sl.Name
            """;
        cmd.Parameters.AddWithValue("$d", deckId);
        using var r = cmd.ExecuteReader();
        var items = new List<ShoppingItem>();
        while (r.Read()) items.Add(ReadShoppingItem(r));
        return items;
    }

    /// <summary>Returns true if the shopping list item is already tagged to this deck.</summary>
    public bool IsShoppingItemTaggedToDeck(int shoppingListId, int deckId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM DeckShoppingItems WHERE DeckId=$d AND ShoppingListId=$s";
        cmd.Parameters.AddWithValue("$d", deckId);
        cmd.Parameters.AddWithValue("$s", shoppingListId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static ShoppingItem ReadShoppingItem(SqliteDataReader r)
    {
        var priceOrd   = r.GetOrdinal("CardCurrentMarketPrice");
        var priceAtOrd = r.GetOrdinal("CardCurrentMarketPriceFetchedAt");
        return new ShoppingItem
        {
            Id              = r.GetInt32(r.GetOrdinal("Id")),
            ScryfallId      = r.GetString(r.GetOrdinal("ScryfallId")),
            Name            = r.GetString(r.GetOrdinal("Name")),
            SetCode         = r.GetString(r.GetOrdinal("SetCode")),
            SetName         = r.GetString(r.GetOrdinal("SetName")),
            CollectorNumber = r.GetString(r.GetOrdinal("CollectorNumber")),
            ManaCost        = r.GetString(r.GetOrdinal("ManaCost")),
            TypeLine        = r.GetString(r.GetOrdinal("TypeLine")),
            ColorIdentity   = r.GetString(r.GetOrdinal("ColorIdentity")),
            Rarity          = r.GetString(r.GetOrdinal("Rarity")),
            Added           = DateTime.Parse(r.GetString(r.GetOrdinal("Added"))),
            PlaceholderCardId            = r.IsDBNull(r.GetOrdinal("PlaceholderCardId")) ? null : r.GetInt32(r.GetOrdinal("PlaceholderCardId")),
            CurrentMarketPrice           = r.IsDBNull(priceOrd)   ? null : r.GetDecimal(priceOrd),
            CurrentMarketPriceFetchedAt  = r.IsDBNull(priceAtOrd) ? null : DateTime.Parse(r.GetString(priceAtOrd))
        };
    }

    // --- Decks ---

    public List<Deck> GetAllDecks()
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT d.*, c.Name AS CommanderName, c.ScryfallId AS CommanderScryfallId
            FROM Decks d
            LEFT JOIN DeckCards dc ON dc.DeckId = d.Id AND dc.IsCommander = 1
            LEFT JOIN Cards c ON c.Id = dc.CardId
            ORDER BY d.Name
            """;
        using var reader = cmd.ExecuteReader();
        var decks = new List<Deck>();
        while (reader.Read())
        {
            var deck = ReadDeck(reader);
            var nameOrdinal = reader.GetOrdinal("CommanderName");
            if (!reader.IsDBNull(nameOrdinal))
                deck.CommanderName = reader.GetString(nameOrdinal);
            var sidOrdinal = reader.GetOrdinal("CommanderScryfallId");
            if (!reader.IsDBNull(sidOrdinal))
                deck.CommanderScryfallId = reader.GetString(sidOrdinal);
            decks.Add(deck);
        }
        return decks;
    }

    /// <summary>Returns the names of all decks that have tagged a shopping list item as a wishlist upgrade.</summary>
    public List<string> GetWishlistDeckNamesForShoppingItem(int shoppingListId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT d.Name FROM Decks d
            INNER JOIN DeckShoppingItems dsi ON dsi.DeckId = d.Id
            WHERE dsi.ShoppingListId = $sid
            ORDER BY d.Name
            """;
        cmd.Parameters.AddWithValue("$sid", shoppingListId);
        using var r = cmd.ExecuteReader();
        var names = new List<string>();
        while (r.Read()) names.Add(r.GetString(0));
        return names;
    }

    /// <summary>Returns the names of all decks that contain a given card (by Cards.Id).</summary>
    public List<string> GetDeckNamesForCard(int cardId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT d.Name FROM Decks d
            INNER JOIN DeckCards dc ON dc.DeckId = d.Id
            WHERE dc.CardId = $cid
            ORDER BY d.Name
            """;
        cmd.Parameters.AddWithValue("$cid", cardId);
        using var r = cmd.ExecuteReader();
        var names = new List<string>();
        while (r.Read()) names.Add(r.GetString(0));
        return names;
    }

    public Deck? GetDeckWithCards(int deckId)
    {
        using var conn = CreateConnection();
        conn.Open();
        Deck? deck = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM Decks WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", deckId);
            using var r = cmd.ExecuteReader();
            if (r.Read()) deck = ReadDeck(r);
        }
        if (deck == null) return null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT dc.Id AS DeckCardId, dc.DeckId, dc.CardId, dc.Quantity, dc.IsSideboard, dc.IsCommander, c.*
                FROM DeckCards dc
                JOIN Cards c ON c.Id = dc.CardId
                WHERE dc.DeckId = $deckId
                """;
            cmd.Parameters.AddWithValue("$deckId", deckId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                deck.Cards.Add(new DeckCard
                {
                    Id = r.GetInt32(r.GetOrdinal("DeckCardId")),
                    DeckId = deckId,
                    CardId = r.GetInt32(r.GetOrdinal("CardId")),
                    Quantity = r.GetInt32(r.GetOrdinal("Quantity")),
                    IsSideboard = r.GetInt32(r.GetOrdinal("IsSideboard")) == 1,
                    IsCommander = r.GetInt32(r.GetOrdinal("IsCommander")) == 1,
                    Card = ReadCard(r)
                });
            }
        }
        return deck;
    }

    public int AddDeck(Deck deck)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Decks (Name, Description, Format, Created)
            VALUES ($name, $desc, $format, $created);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", deck.Name);
        cmd.Parameters.AddWithValue("$desc", (object?)deck.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$format", (object?)deck.Format ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", deck.Created.ToString("o"));
        deck.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return deck.Id;
    }

    public void UpdateDeck(Deck deck)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Decks SET Name=$name, Description=$desc, Format=$format, CommanderColorIdentity=$cci WHERE Id=$id";
        cmd.Parameters.AddWithValue("$name", deck.Name);
        cmd.Parameters.AddWithValue("$desc", (object?)deck.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$format", (object?)deck.Format ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cci", (object?)deck.CommanderColorIdentity ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", deck.Id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteDeck(int id)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Decks WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void AddCardToDeck(int deckId, int cardId, int quantity, bool isSideboard, bool isCommander = false)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO DeckCards (DeckId, CardId, Quantity, IsSideboard, IsCommander)
            VALUES ($deckId, $cardId, $qty, $side, $isCommander)
            """;
        cmd.Parameters.AddWithValue("$deckId", deckId);
        cmd.Parameters.AddWithValue("$cardId", cardId);
        cmd.Parameters.AddWithValue("$qty", quantity);
        cmd.Parameters.AddWithValue("$side", isSideboard ? 1 : 0);
        cmd.Parameters.AddWithValue("$isCommander", isCommander ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public void RemoveCardFromDeck(int deckCardId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DeckCards WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", deckCardId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Decrements the quantity of a deck card by 1, deleting the row when it reaches 0.</summary>
    public void DecrementDeckCard(int deckCardId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using var selectCmd = conn.CreateCommand();
            selectCmd.Transaction = tx;
            selectCmd.CommandText = "SELECT Quantity FROM DeckCards WHERE Id = $id";
            selectCmd.Parameters.AddWithValue("$id", deckCardId);
            var qty = selectCmd.ExecuteScalar();
            if (qty == null || qty is DBNull) { tx.Rollback(); return; }

            using var mutCmd = conn.CreateCommand();
            mutCmd.Transaction = tx;
            if (Convert.ToInt32(qty) > 1)
            {
                mutCmd.CommandText = "UPDATE DeckCards SET Quantity = Quantity - 1 WHERE Id = $id";
            }
            else
            {
                mutCmd.CommandText = "DELETE FROM DeckCards WHERE Id = $id";
            }
            mutCmd.Parameters.AddWithValue("$id", deckCardId);
            mutCmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Increments the quantity of an existing deck card row by 1.</summary>
    public void IncrementDeckCardQuantity(int deckCardId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE DeckCards SET Quantity = Quantity + 1 WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", deckCardId);
        cmd.ExecuteNonQuery();
    }

    public void SetDeckCardCommander(int deckId, int? commanderDeckCardId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Clear all commander flags for this deck first
        cmd.CommandText = "UPDATE DeckCards SET IsCommander = 0 WHERE DeckId = $deckId";
        cmd.Parameters.AddWithValue("$deckId", deckId);
        cmd.ExecuteNonQuery();
        if (commanderDeckCardId.HasValue)
        {
            cmd.CommandText = "UPDATE DeckCards SET IsCommander = 1 WHERE Id = $id";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$id", commanderDeckCardId.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private static Deck ReadDeck(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Name = r.GetString(r.GetOrdinal("Name")),
        Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
        Format = r.IsDBNull(r.GetOrdinal("Format")) ? null : r.GetString(r.GetOrdinal("Format")),
        Created = DateTime.Parse(r.GetString(r.GetOrdinal("Created"))),
        CommanderColorIdentity = r.IsDBNull(r.GetOrdinal("CommanderColorIdentity")) ? null : r.GetString(r.GetOrdinal("CommanderColorIdentity"))
    };

    // --- Deck versions ---

    public int CreateDeckSnapshot(int deckId, string? label, bool isAuto)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        var versionId = CreateDeckSnapshotCore(conn, tx, deckId, label, isAuto);
        tx.Commit();
        return versionId;
    }

    private int CreateDeckSnapshotCore(SqliteConnection conn, SqliteTransaction tx, int deckId, string? label, bool isAuto)
    {
        int versionNumber;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COALESCE(MAX(VersionNumber), 0) + 1 FROM DeckVersions WHERE DeckId = $deckId";
            cmd.Parameters.AddWithValue("$deckId", deckId);
            versionNumber = Convert.ToInt32(cmd.ExecuteScalar());
        }

        int versionId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO DeckVersions (DeckId, VersionNumber, Label, CreatedAt, IsAuto)
                VALUES ($deckId, $vn, $label, $at, $isAuto);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$deckId", deckId);
            cmd.Parameters.AddWithValue("$vn", versionNumber);
            cmd.Parameters.AddWithValue("$label", (object?)label ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$isAuto", isAuto ? 1 : 0);
            versionId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO DeckVersionCards (VersionId, CardId, CardName, SetCode, CollectorNumber, Quantity, IsSideboard, IsCommander)
                SELECT $versionId, dc.CardId, c.Name, c.SetCode, c.CollectorNumber, dc.Quantity, dc.IsSideboard, dc.IsCommander
                FROM DeckCards dc
                JOIN Cards c ON c.Id = dc.CardId
                WHERE dc.DeckId = $deckId
                """;
            cmd.Parameters.AddWithValue("$versionId", versionId);
            cmd.Parameters.AddWithValue("$deckId", deckId);
            cmd.ExecuteNonQuery();
        }

        return versionId;
    }

    /// <summary>Returns the latest version ID for this deck, creating an auto-snapshot only if the deck changed since the last version.</summary>
    public int GetOrCreateCurrentSnapshot(int deckId)
    {
        using var conn = CreateConnection();
        conn.Open();

        // Get latest version
        int? latestVersionId = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id FROM DeckVersions WHERE DeckId = $deckId ORDER BY VersionNumber DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$deckId", deckId);
            var result = cmd.ExecuteScalar();
            if (result != null && result != DBNull.Value)
                latestVersionId = Convert.ToInt32(result);
        }

        if (latestVersionId.HasValue && !DeckChangedSinceVersion(conn, deckId, latestVersionId.Value))
            return latestVersionId.Value;

        using var tx = conn.BeginTransaction();
        var newId = CreateDeckSnapshotCore(conn, tx, deckId, null, isAuto: true);
        tx.Commit();
        return newId;
    }

    private bool DeckChangedSinceVersion(SqliteConnection conn, int deckId, int versionId)
    {
        // Compare current DeckCards with DeckVersionCards for this version
        // They match if the set of (CardId, Quantity, IsSideboard, IsCommander) is identical
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM (
                SELECT CardId, Quantity, IsSideboard, IsCommander FROM DeckCards WHERE DeckId = $deckId
                EXCEPT
                SELECT CardId, Quantity, IsSideboard, IsCommander FROM DeckVersionCards WHERE VersionId = $versionId
            ) UNION ALL
            SELECT COUNT(*) FROM (
                SELECT CardId, Quantity, IsSideboard, IsCommander FROM DeckVersionCards WHERE VersionId = $versionId
                EXCEPT
                SELECT CardId, Quantity, IsSideboard, IsCommander FROM DeckCards WHERE DeckId = $deckId
            )
            """;
        cmd.Parameters.AddWithValue("$deckId", deckId);
        cmd.Parameters.AddWithValue("$versionId", versionId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (r.GetInt32(0) > 0) return true;
        return false;
    }

    public List<DeckVersion> GetDeckVersions(int deckId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM DeckVersions WHERE DeckId = $deckId ORDER BY VersionNumber DESC";
        cmd.Parameters.AddWithValue("$deckId", deckId);
        using var r = cmd.ExecuteReader();
        var versions = new List<DeckVersion>();
        while (r.Read())
            versions.Add(ReadDeckVersion(r));
        return versions;
    }

    public List<DeckVersionCard> GetDeckVersionCards(int versionId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM DeckVersionCards WHERE VersionId = $versionId ORDER BY CardName";
        cmd.Parameters.AddWithValue("$versionId", versionId);
        using var r = cmd.ExecuteReader();
        var cards = new List<DeckVersionCard>();
        while (r.Read())
            cards.Add(new DeckVersionCard
            {
                Id = r.GetInt32(r.GetOrdinal("Id")),
                VersionId = versionId,
                CardId = r.GetInt32(r.GetOrdinal("CardId")),
                CardName = r.GetString(r.GetOrdinal("CardName")),
                SetCode = r.GetString(r.GetOrdinal("SetCode")),
                CollectorNumber = r.GetString(r.GetOrdinal("CollectorNumber")),
                Quantity = r.GetInt32(r.GetOrdinal("Quantity")),
                IsSideboard = r.GetInt32(r.GetOrdinal("IsSideboard")) == 1,
                IsCommander = r.GetInt32(r.GetOrdinal("IsCommander")) == 1
            });
        return cards;
    }

    private static DeckVersion ReadDeckVersion(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        DeckId = r.GetInt32(r.GetOrdinal("DeckId")),
        VersionNumber = r.GetInt32(r.GetOrdinal("VersionNumber")),
        Label = r.IsDBNull(r.GetOrdinal("Label")) ? null : r.GetString(r.GetOrdinal("Label")),
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        IsAuto = r.GetInt32(r.GetOrdinal("IsAuto")) == 1
    };

    // --- Binders ---

    public List<Binder> GetAllBinders()
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Binders ORDER BY Name";
        using var reader = cmd.ExecuteReader();
        var binders = new List<Binder>();
        while (reader.Read()) binders.Add(ReadBinder(reader));
        return binders;
    }

    public Binder? GetBinderWithCards(int binderId)
    {
        using var conn = CreateConnection();
        conn.Open();
        Binder? binder = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM Binders WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", binderId);
            using var r = cmd.ExecuteReader();
            if (r.Read()) binder = ReadBinder(r);
        }
        if (binder == null) return null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT bc.*, c.* FROM BinderCards bc
                JOIN Cards c ON c.Id = bc.CardId
                WHERE bc.BinderId = $binderId
                ORDER BY bc.SlotIndex
                """;
            cmd.Parameters.AddWithValue("$binderId", binderId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                binder.Cards.Add(new BinderCard
                {
                    Id = r.GetInt32(r.GetOrdinal("Id")),
                    BinderId = binderId,
                    CardId = r.GetInt32(r.GetOrdinal("CardId")),
                    Quantity = r.GetInt32(r.GetOrdinal("Quantity")),
                    SlotIndex = r.GetInt32(r.GetOrdinal("SlotIndex")),
                    Card = ReadCard(r)
                });
            }
        }
        return binder;
    }

    public int AddBinder(Binder binder)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Binders (Name, Description, Created)
            VALUES ($name, $desc, $created);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", binder.Name);
        cmd.Parameters.AddWithValue("$desc", (object?)binder.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", binder.Created.ToString("o"));
        binder.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return binder.Id;
    }

    public void UpdateBinder(Binder binder)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Binders SET Name=$name, Description=$desc WHERE Id=$id";
        cmd.Parameters.AddWithValue("$name", binder.Name);
        cmd.Parameters.AddWithValue("$desc", (object?)binder.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", binder.Id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteBinder(int id)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Binders WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void AddCardToBinder(int binderId, int cardId, int quantity, int slotIndex)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO BinderCards (BinderId, CardId, Quantity, SlotIndex)
            VALUES ($binderId, $cardId, $qty, $slot)
            """;
        cmd.Parameters.AddWithValue("$binderId", binderId);
        cmd.Parameters.AddWithValue("$cardId", cardId);
        cmd.Parameters.AddWithValue("$qty", quantity);
        cmd.Parameters.AddWithValue("$slot", slotIndex);
        cmd.ExecuteNonQuery();
    }

    public void RemoveCardFromBinder(int binderCardId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM BinderCards WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", binderCardId);
        cmd.ExecuteNonQuery();
    }

    private static Binder ReadBinder(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Name = r.GetString(r.GetOrdinal("Name")),
        Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
        Created = DateTime.Parse(r.GetString(r.GetOrdinal("Created")))
    };

    // --- Games ---

    public int AddGame(Game game)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        int gameId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Games (PlayedAt, TurnEnded, Notes)
                VALUES ($playedAt, $turnEnded, $notes);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$playedAt", game.PlayedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$turnEnded", (object?)game.TurnEnded ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$notes", (object?)game.Notes ?? DBNull.Value);
            gameId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        foreach (var p in game.Players)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO GamePlayers (GameId, PlayerName, IsMe, DeckId, DeckName, DeckVersionId, FinishPosition)
                VALUES ($gameId, $name, $isMe, $deckId, $deckName, $versionId, $pos)
                """;
            cmd.Parameters.AddWithValue("$gameId", gameId);
            cmd.Parameters.AddWithValue("$name", p.PlayerName);
            cmd.Parameters.AddWithValue("$isMe", p.IsMe ? 1 : 0);
            cmd.Parameters.AddWithValue("$deckId", (object?)p.DeckId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$deckName", (object?)p.DeckName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$versionId", (object?)p.DeckVersionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pos", p.FinishPosition);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return gameId;
    }

    public List<Game> GetAllGames()
    {
        using var conn = CreateConnection();
        conn.Open();
        var games = new List<Game>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM Games ORDER BY PlayedAt DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read()) games.Add(ReadGame(r));
        }

        foreach (var game in games)
            game.Players = GetGamePlayers(conn, game.Id);

        return games;
    }

    public List<Game> GetGamesForDeck(int deckId)
    {
        using var conn = CreateConnection();
        conn.Open();
        var games = new List<Game>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT DISTINCT g.* FROM Games g
                JOIN GamePlayers gp ON gp.GameId = g.Id
                WHERE gp.DeckId = $deckId
                ORDER BY g.PlayedAt DESC
                """;
            cmd.Parameters.AddWithValue("$deckId", deckId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) games.Add(ReadGame(r));
        }

        foreach (var game in games)
            game.Players = GetGamePlayers(conn, game.Id);

        return games;
    }

    public void UpdateGame(Game game)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Games SET PlayedAt=$playedAt, TurnEnded=$turnEnded, Notes=$notes WHERE Id=$id";
            cmd.Parameters.AddWithValue("$playedAt", game.PlayedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$turnEnded", (object?)game.TurnEnded ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$notes", (object?)game.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", game.Id);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM GamePlayers WHERE GameId=$id";
            cmd.Parameters.AddWithValue("$id", game.Id);
            cmd.ExecuteNonQuery();
        }

        foreach (var p in game.Players)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO GamePlayers (GameId, PlayerName, IsMe, DeckId, DeckName, DeckVersionId, FinishPosition)
                VALUES ($gameId, $name, $isMe, $deckId, $deckName, $versionId, $pos)
                """;
            cmd.Parameters.AddWithValue("$gameId", game.Id);
            cmd.Parameters.AddWithValue("$name", p.PlayerName);
            cmd.Parameters.AddWithValue("$isMe", p.IsMe ? 1 : 0);
            cmd.Parameters.AddWithValue("$deckId", (object?)p.DeckId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$deckName", (object?)p.DeckName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$versionId", (object?)p.DeckVersionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pos", p.FinishPosition);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void DeleteGame(int id)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Games WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns win/loss stats for a deck (games where my player used this deck).</summary>
    public (int Wins, int Losses, double? AvgWinTurn, double? AvgLossTurn) GetDeckGameStats(int deckId)
    {
        using var conn = CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT gp.FinishPosition, g.TurnEnded
            FROM GamePlayers gp
            JOIN Games g ON g.Id = gp.GameId
            WHERE gp.DeckId = $deckId AND gp.IsMe = 1
            """;
        cmd.Parameters.AddWithValue("$deckId", deckId);

        int wins = 0, losses = 0;
        var winTurns = new List<int>();
        var lossTurns = new List<int>();

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var pos = r.GetInt32(r.GetOrdinal("FinishPosition"));
            var turnNull = r.IsDBNull(r.GetOrdinal("TurnEnded"));
            var turn = turnNull ? (int?)null : r.GetInt32(r.GetOrdinal("TurnEnded"));

            if (pos == 1)
            {
                wins++;
                if (turn.HasValue) winTurns.Add(turn.Value);
            }
            else
            {
                losses++;
                if (turn.HasValue) lossTurns.Add(turn.Value);
            }
        }

        double? avgWin = winTurns.Count > 0 ? winTurns.Average() : null;
        double? avgLoss = lossTurns.Count > 0 ? lossTurns.Average() : null;
        return (wins, losses, avgWin, avgLoss);
    }

    private static List<GamePlayer> GetGamePlayers(SqliteConnection conn, int gameId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT gp.*, d.Name AS LinkedDeckName, dv.VersionNumber AS DeckVersionNumber,
                   (SELECT c.ScryfallId FROM DeckCards dc
                    JOIN Cards c ON c.Id = dc.CardId
                    WHERE dc.DeckId = gp.DeckId AND dc.IsCommander = 1
                    LIMIT 1) AS CommanderScryfallId
            FROM GamePlayers gp
            LEFT JOIN Decks d ON d.Id = gp.DeckId
            LEFT JOIN DeckVersions dv ON dv.Id = gp.DeckVersionId
            WHERE gp.GameId = $gameId
            ORDER BY gp.FinishPosition
            """;
        cmd.Parameters.AddWithValue("$gameId", gameId);
        using var r = cmd.ExecuteReader();
        var players = new List<GamePlayer>();
        while (r.Read())
        {
            var deckIdOrd = r.GetOrdinal("DeckId");
            var deckId = r.IsDBNull(deckIdOrd) ? (int?)null : r.GetInt32(deckIdOrd);
            var linkedNameOrd = r.GetOrdinal("LinkedDeckName");
            var linkedName = r.IsDBNull(linkedNameOrd) ? null : r.GetString(linkedNameOrd);
            var deckNameOrd = r.GetOrdinal("DeckName");
            var deckName = r.IsDBNull(deckNameOrd) ? null : r.GetString(deckNameOrd);
            var versionIdOrd = r.GetOrdinal("DeckVersionId");
            var versionId = r.IsDBNull(versionIdOrd) ? (int?)null : r.GetInt32(versionIdOrd);
            var versionNumOrd = r.GetOrdinal("DeckVersionNumber");
            var versionNum = r.IsDBNull(versionNumOrd) ? (int?)null : r.GetInt32(versionNumOrd);
            var cmdScryfallOrd = r.GetOrdinal("CommanderScryfallId");
            var cmdScryfallId = r.IsDBNull(cmdScryfallOrd) ? null : r.GetString(cmdScryfallOrd);

            players.Add(new GamePlayer
            {
                Id = r.GetInt32(r.GetOrdinal("Id")),
                GameId = gameId,
                PlayerName = r.GetString(r.GetOrdinal("PlayerName")),
                IsMe = r.GetInt32(r.GetOrdinal("IsMe")) == 1,
                DeckId = deckId,
                DeckName = linkedName ?? deckName,
                DeckVersionId = versionId,
                DeckVersionNumber = versionNum,
                FinishPosition = r.GetInt32(r.GetOrdinal("FinishPosition")),
                CommanderScryfallId = cmdScryfallId,
                Deck = deckId.HasValue && linkedName != null ? new Models.Deck { Id = deckId.Value, Name = linkedName } : null
            });
        }
        return players;
    }

    private static Game ReadGame(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        PlayedAt = DateTime.Parse(r.GetString(r.GetOrdinal("PlayedAt"))),
        TurnEnded = r.IsDBNull(r.GetOrdinal("TurnEnded")) ? null : r.GetInt32(r.GetOrdinal("TurnEnded")),
        Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes"))
    };
}
