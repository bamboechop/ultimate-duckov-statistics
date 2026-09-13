using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Sqlite;

// Growing crafting dictionaries use typed rows. Integer SUM is exact here:
// every term is nonnegative and SQLite rejects 64-bit overflow, matching the
// existing checked reducer. Doubles and decimals never pass through SQL SUM.
internal static class SqliteCraftingRecords
{
    internal static bool Owns(ProfileRecordKind kind) => kind >= ProfileRecordKind.CraftingOutput && kind <= ProfileRecordKind.CraftingAssociation;

    internal static void Create(SqliteStore db)
    {
        db.Exec("CREATE TABLE crafting(ordinal INTEGER PRIMARY KEY,kind INTEGER NOT NULL,k1 TEXT NOT NULL,k2 TEXT NOT NULL,k3 TEXT NOT NULL,name TEXT NOT NULL,actions INTEGER NOT NULL CHECK(actions>=0),quantity INTEGER NOT NULL CHECK(quantity>=0),charge_actions INTEGER NOT NULL CHECK(charge_actions>=0),charged INTEGER NOT NULL CHECK(charged>=0),UNIQUE(kind,k1,k2,k3))");
        db.Exec("CREATE INDEX crafting_resource_fanout ON crafting(kind,k3)");
    }

    internal static void Put(SqliteStore db, ProfileRecordChange change)
    {
        var key = change.Address;
        if (change.Bytes == null) { db.Exec("DELETE FROM crafting WHERE kind=? AND k1=? AND k2=? AND k3=?", (int)key.Kind, key.First, key.Second, key.Third); return; }
        var value = ProfileRecordCodec.Decode(change.Bytes, ProfileRecordCapture.RecordType(key.Kind));
        string name = ""; long actions = 0, quantity = 0, charges = 0, charged = 0;
        switch (value)
        {
            case CraftedOutputAggregate output: name = output.DisplayName; actions = output.CompletionActions; quantity = output.ProducedQuantity; charges = output.CurrencyChargeActions; charged = output.CurrencyCharged; break;
            case CraftingRecipeAggregate recipe: actions = recipe.CompletionActions; quantity = recipe.ProducedQuantity; charges = recipe.CurrencyChargeActions; charged = recipe.CurrencyCharged; break;
            case long count: actions = count; break;
            case CraftingResourceAggregate resource: name = resource.DisplayName; quantity = resource.ConsumedQuantity; break;
            case CraftingResourceAssociationAggregate resource: name = resource.DisplayName; actions = resource.ConsumptionActions; quantity = resource.ConsumedQuantity; break;
            default: throw new InvalidDataException("Unexpected crafting record.");
        }
        db.Exec("INSERT INTO crafting(kind,k1,k2,k3,name,actions,quantity,charge_actions,charged) VALUES(?,?,?,?,?,?,?,?,?) ON CONFLICT(kind,k1,k2,k3) DO UPDATE SET name=excluded.name,actions=excluded.actions,quantity=excluded.quantity,charge_actions=excluded.charge_actions,charged=excluded.charged",
            (int)key.Kind, key.First, key.Second, key.Third, name, actions, quantity, charges, charged);
    }

    internal static IEnumerable<ProfileRecordChange> Read(SqliteStore db, ProfileRecordKind recordKind)
    {
        var codec = new ProfileRecordCodec();
        foreach (var row in db.EnumerateRows("SELECT kind,k1,k2,k3,name,actions,quantity,charge_actions,charged FROM crafting WHERE kind=? ORDER BY ordinal", (int)recordKind))
        {
            var kind = (ProfileRecordKind)(long)row[0];
            var key = new ProfileRecordAddress(kind, (string)row[1], (string)row[2], (string)row[3]);
            var name = (string)row[4]; var actions = (long)row[5]; var quantity = (long)row[6]; var charges = (long)row[7]; var charged = (long)row[8];
            object value = kind switch
            {
                ProfileRecordKind.CraftingOutput => new CraftedOutputAggregate { OutputItemId = key.First, DisplayName = name, CompletionActions = actions, ProducedQuantity = quantity, CurrencyChargeActions = charges, CurrencyCharged = charged },
                ProfileRecordKind.CraftingRecipe => new CraftingRecipeAggregate { RecipeId = key.Second, CompletionActions = actions, ProducedQuantity = quantity, CurrencyChargeActions = charges, CurrencyCharged = charged },
                ProfileRecordKind.CraftingBatch => actions,
                ProfileRecordKind.CraftingResource => new CraftingResourceAggregate { ResourceItemId = key.First, DisplayName = name, ConsumedQuantity = quantity },
                ProfileRecordKind.CraftingAssociation => new CraftingResourceAssociationAggregate { ResourceItemId = key.Third, DisplayName = name, ConsumptionActions = actions, ConsumedQuantity = quantity },
                _ => throw new InvalidDataException("Unsupported crafting row kind.")
            };
            yield return new ProfileRecordChange(key, 0, codec.Encode(value, value.GetType()));
        }
    }

    internal static bool ScopeChanged(SqliteStore db, IncrementalProfileWrite write)
    {
        var change = write.Records.FirstOrDefault(row => row.Address.Kind == ProfileRecordKind.Crafting);
        if (change?.Bytes == null) return false;
        var previousBytes = SqliteProfileStorage.ReadRootPayload(db, 9);
        if (previousBytes == null) return true;
        var previous = ProfileRecordCodec.Decode<CraftingStatisticsAggregate>(previousBytes);
        var next = ProfileRecordCodec.Decode<CraftingStatisticsAggregate>(change.Bytes);
        return previous.Capabilities.RecipeIdentity.State != next.Capabilities.RecipeIdentity.State
            || previous.Capabilities.ProducedQuantity.State != next.Capabilities.ProducedQuantity.State
            || previous.Capabilities.BatchMetadata.State != next.Capabilities.BatchMetadata.State
            || previous.QuantityArithmeticUnavailable != next.QuantityArithmeticUnavailable
            || previous.ResourceActionArithmeticUnavailable != next.ResourceActionArithmeticUnavailable
            || previous.ResourceQuantityArithmeticUnavailable != next.ResourceQuantityArithmeticUnavailable
            || previous.CurrencyActionArithmeticUnavailable != next.CurrencyActionArithmeticUnavailable
            || previous.CurrencyAmountArithmeticUnavailable != next.CurrencyAmountArithmeticUnavailable;
    }

    internal static void ValidateAffected(SqliteStore db, IncrementalProfileWrite write, bool scopeChanged)
    {
        if (!write.Records.Any(row => row.Address.Kind == ProfileRecordKind.Crafting || Owns(row.Address.Kind))) return;
        var header = ProfileRecordCodec.Decode<CraftingStatisticsAggregate>(SqliteProfileStorage.ReadRootPayload(db, 9)
            ?? throw new InvalidDataException("Crafting header is missing."));
        CraftingStatisticsReducer.ValidateHeader(header);
        // A capability/arithmetic transition can change the validity rules for
        // existing children. Revisit those keys only on that uncommon boundary.
        IEnumerable<ProfileRecordChange> affected = write.Records;
        if (scopeChanged) affected = affected.Concat(db.Rows("SELECT kind,k1,k2,k3 FROM crafting").Select(row =>
            new ProfileRecordChange(new ProfileRecordAddress((ProfileRecordKind)(long)row[0], (string)row[1], (string)row[2], (string)row[3]), 0, null)));
        var affectedRecords = affected.ToArray();
        var outputTotals = Totals(db, "kind=10");
        Equal(outputTotals, header.CompletionActions, header.ProducedQuantity, header.CurrencyChargeActions, header.CurrencyCharged);
        foreach (var outputId in affectedRecords.Where(row => Owns(row.Address.Kind) && row.Address.Kind != ProfileRecordKind.CraftingResource).Select(row => row.Address.First).Distinct())
        {
            var output = Required(db, 10, outputId, "", "");
            var recipes = Totals(db, "kind=11 AND k1=?", outputId);
            CraftingStatisticsReducer.ValidateComposition(header.Capabilities.RecipeIdentity, recipes[0], output[0], recipes[1], output[1], "Crafting recipe composition is inconsistent.");
            if (recipes[2] != output[2] || recipes[3] != output[3]) throw new InvalidDataException("Crafting recipe currency composition is inconsistent.");
            Currency(header, output);
        }
        foreach (var key in affectedRecords.Where(row => row.Address.Kind is ProfileRecordKind.CraftingRecipe or ProfileRecordKind.CraftingBatch or ProfileRecordKind.CraftingAssociation).Select(row => (row.Address.First, row.Address.Second)).Distinct())
        {
            var totals = Required(db, 11, key.First, key.Second, "");
            Currency(header, totals);
            var recipe = new CraftingRecipeAggregate { CompletionActions = totals[0], ProducedQuantity = totals[1] };
            foreach (var batch in db.Rows("SELECT k3,actions FROM crafting WHERE kind=12 AND k1=? AND k2=?", key.First, key.Second)) recipe.BatchActions.Add((string)batch[0], (long)batch[1]);
            CraftingStatisticsReducer.ValidateBatches(header, recipe);
            foreach (var association in db.Rows("SELECT actions,quantity FROM crafting WHERE kind=14 AND k1=? AND k2=?", key.First, key.Second))
            {
                var actions = (long)association[0]; var quantity = (long)association[1];
                if ((!header.ResourceActionArithmeticUnavailable && actions == 0) || (!header.ResourceQuantityArithmeticUnavailable && quantity == 0)
                    || (!header.ResourceQuantityArithmeticUnavailable && actions > quantity) || actions > recipe.CompletionActions)
                    throw new InvalidDataException("Crafting resource association is invalid.");
            }
        }
        foreach (var resourceId in affectedRecords.Where(row => row.Address.Kind is ProfileRecordKind.CraftingResource or ProfileRecordKind.CraftingAssociation)
                     .Select(row => row.Address.Kind == ProfileRecordKind.CraftingResource ? row.Address.First : row.Address.Third).Distinct())
        {
            var total = db.Rows("SELECT quantity FROM crafting WHERE kind=13 AND k1=?", resourceId);
            var associated = db.ScalarLong("SELECT COALESCE(SUM(quantity),0) FROM crafting WHERE kind=14 AND k3=?", resourceId);
            if (total.Count == 0 ? (!header.ResourceQuantityArithmeticUnavailable || associated != 0) : ((long)total[0][0] <= 0 || (long)total[0][0] != associated))
                throw new InvalidDataException("Crafting resource total and associations disagree.");
        }
    }

    private static long[] Totals(SqliteStore db, string condition, params object?[] values) => db.Rows(
        "SELECT COALESCE(SUM(actions),0),COALESCE(SUM(quantity),0),COALESCE(SUM(charge_actions),0),COALESCE(SUM(charged),0) FROM crafting WHERE " + condition, values)[0].Cast<long>().ToArray();
    private static long[] Required(SqliteStore db, int kind, string first, string second, string third)
    {
        var rows = db.Rows("SELECT actions,quantity,charge_actions,charged FROM crafting WHERE kind=? AND k1=? AND k2=? AND k3=?", kind, first, second, third);
        if (rows.Count != 1) throw new InvalidDataException("A crafting child has no parent.");
        return rows[0].Cast<long>().ToArray();
    }
    private static void Equal(long[] values, long actions, long quantity, long charges, long charged)
    { if (values[0] != actions || values[1] != quantity || values[2] != charges || values[3] != charged) throw new InvalidDataException("Crafting output composition is inconsistent."); }
    private static void Currency(CraftingStatisticsAggregate header, long[] values)
    { if (values[2] > values[0] || CraftingStatisticsReducer.HasImpossibleCurrencyPair(header, values[2], values[3])) throw new InvalidDataException("Crafting currency pair is invalid."); }
}
