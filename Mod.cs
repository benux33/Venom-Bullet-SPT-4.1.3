using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace Venom;

public sealed record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.bensburnedwaffles.venom";
    public string Name { get; init; } = "5.56x45 Venom";
    public string Author { get; init; } = "BensBurnedWaffles";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.0.6");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.2");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 1)]
public sealed class VenomMod : IOnLoad
{
    public const string TemplateId = "a8e1e834c1701e11003c59ef";

    private const string HpTemplateId = "59e6927d86f77411da468256";
    private const string TraderOfferId = "a8e1e834c1701e11003c59f0";
    private const string AmmoParentId = "5485a8684bdc2da71d8b4567";
    private const string AmmoHandbookParentId = "5b47574386f77428ca22b33b";
    private const string JaegerId = "5c0647fdd443bc2504c2d371";
    private const string RoubleId = "5449016a4bdc2d6f028b456f";
    private const int Price = 22_000;
    private const int BuyLimit = 40;
    private const int LoyaltyLevel = 2;

    private readonly CustomItemService _customItemService;
    private readonly TemplateTable _templates;
    private readonly TradersTable _traders;

    public VenomMod(
        CustomItemService customItemService,
        TemplateTable templates,
        TradersTable traders)
    {
        _customItemService = customItemService;
        _templates = templates;
        _traders = traders;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CreateCartridge();

        if (AddToHpCompatibleSlots() == 0)
        {
            throw new InvalidOperationException(
                "5.56x45 Venom could not find a 5.56 HP-compatible chamber or magazine.");
        }

        AddJaegerOffer();
        return Task.CompletedTask;
    }

    private void CreateCartridge()
    {
        NewItemFromCloneDetails details = new()
        {
            NewItemName = "patron_556x45_venom",
            ItemTplToClone = HpTemplateId,
            ParentId = AmmoParentId,
            NewId = TemplateId,
            HandbookParentId = AmmoHandbookParentId,
            HandbookPriceRoubles = Price,
            FleaPriceRoubles = Price,
            AddToHandbook = true,
            AddToFleaPriceDb = true,
            AddToWeaponShelf = false,
            Locales = new Dictionary<string, LocaleDetails>
            {
                ["en"] = new LocaleDetails
                {
                    Name = "5.56x45 Venom",
                    ShortName = "Venom",
                    Description =
                        "A rare experimental cartridge loaded with a highly unstable venom compound. Once the projectile penetrates armor or exposed tissue, the toxin rapidly spreads through the bloodstream, causing nausea, severe weakness, tremors, and progressive organ failure. Without treatment, the victim will eventually succumb to the effects. The toxin can be neutralized if antibiotics are taken quickly enough, making immediate medical treatment essential. Extremely scarce and reportedly produced only in small clandestine batches.",
                },
            },
            OverrideProperties = new TemplateItemProperties
            {
                Damage = 65,
                PenetrationPower = 23,
                LightBleedingDelta = 0.05,
                HeavyBleedingDelta = 0.10,
            },
        };

        CreateItemResult result = _customItemService.CreateItemFromClone(details);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"5.56x45 Venom cartridge creation failed: {string.Join("; ", result.Errors)}");
        }
    }

    private int AddToHpCompatibleSlots()
    {
        MongoId sourceId = HpTemplateId;
        MongoId newId = TemplateId;
        int changedFilters = 0;

        foreach (TemplateItem item in _templates.Items.Values)
        {
            TemplateItemProperties? properties = item.Properties;
            if (properties is null)
            {
                continue;
            }

            changedFilters += AddToSlots(properties.Slots, sourceId, newId);
            changedFilters += AddToSlots(properties.Chambers, sourceId, newId);
            changedFilters += AddToSlots(properties.Cartridges, sourceId, newId);

            if (properties.StackSlots is null)
            {
                continue;
            }

            foreach (StackSlot stackSlot in properties.StackSlots)
            {
                changedFilters += AddToFilters(stackSlot.Properties?.Filters, sourceId, newId);
            }
        }

        return changedFilters;
    }

    private static int AddToSlots(
        IEnumerable<Slot>? slots,
        MongoId sourceId,
        MongoId newId)
    {
        if (slots is null)
        {
            return 0;
        }

        int changedFilters = 0;
        foreach (Slot slot in slots)
        {
            changedFilters += AddToFilters(slot.Properties?.Filters, sourceId, newId);
        }

        return changedFilters;
    }

    private static int AddToFilters(
        IEnumerable<SlotFilter>? filters,
        MongoId sourceId,
        MongoId newId)
    {
        if (filters is null)
        {
            return 0;
        }

        int changedFilters = 0;
        foreach (SlotFilter filter in filters)
        {
            HashSet<MongoId>? acceptedItems = filter.Filter;
            if (acceptedItems is not null && acceptedItems.Contains(sourceId) && acceptedItems.Add(newId))
            {
                changedFilters++;
            }
        }

        return changedFilters;
    }

    private void AddJaegerOffer()
    {
        if (!_traders.TryGetValue(JaegerId, out Trader? jaeger))
        {
            throw new InvalidOperationException(
                "5.56x45 Venom could not find Jaeger in the trader database.");
        }

        MongoId offerId = TraderOfferId;
        jaeger.Assort.Items.Add(new Item
        {
            Id = offerId,
            Template = TemplateId,
            ParentId = "hideout",
            SlotId = "hideout",
            Upd = new Upd
            {
                UnlimitedCount = true,
                StackObjectsCount = 9_999_999,
                BuyRestrictionMax = BuyLimit,
                BuyRestrictionCurrent = 0,
            },
        });

        jaeger.Assort.BarterScheme[offerId] = new List<List<BarterScheme>>
        {
            new()
            {
                new BarterScheme
                {
                    Count = Price,
                    Template = RoubleId,
                },
            },
        };
        jaeger.Assort.LoyalLevelItems[offerId] = LoyaltyLevel;
    }
}
