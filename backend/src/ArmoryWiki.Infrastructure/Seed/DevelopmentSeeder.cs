using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Infrastructure.Seed;

/// <summary>
/// Hand-written development data. Every QID was resolved from its English Wikipedia sitelink, and dates
/// come from Wikidata (inception / birth / death) where Wikidata has them; otherwise they are left null
/// rather than guessed. Specs are left null for the importers to fill (PLAN.md Phase 4).
/// </summary>
public static class DevelopmentSeeder
{
    public static async Task SeedAsync(ArmoryWikiDbContext db, CancellationToken ct = default)
    {
        if (await db.Countries.AnyAsync(ct))
        {
            return;
        }

        // Countries
        Country AddCountry(string slug, string name, string qid, string? iso = null, bool historical = false)
        {
            var c = new Country { Slug = slug, Name = name, WikidataId = qid, IsoCode = iso, IsHistorical = historical };
            db.Countries.Add(c);
            return c;
        }
        var usa = AddCountry("united-states", "United States", "Q30", "US");
        var ussr = AddCountry("soviet-union", "Soviet Union", "Q15180", "SU", historical: true);
        var russia = AddCountry("russia", "Russia", "Q159", "RU");
        var russianEmpire = AddCountry("russian-empire", "Russian Empire", "Q34266", historical: true);
        var germany = AddCountry("germany", "Germany", "Q183", "DE");
        var germanEmpire = AddCountry("german-empire", "German Empire", "Q43287", historical: true);
        var naziGermany = AddCountry("nazi-germany", "Nazi Germany", "Q7318", historical: true);
        AddCountry("east-germany", "East Germany", "Q16957", "DD", historical: true);
        AddCountry("west-germany", "West Germany", "Q713750", historical: true);
        AddCountry("czechoslovakia", "Czechoslovakia", "Q33946", "CS", historical: true);
        AddCountry("yugoslavia", "Yugoslavia", "Q36704", "YU", historical: true);
        var belgium = AddCountry("belgium", "Belgium", "Q31", "BE");
        AddCountry("united-kingdom", "United Kingdom", "Q145", "GB");

        // Mechanisms and tags come from ReferenceDataSeeder, which runs first.
        var mechanisms = await db.OperatingMechanisms.ToDictionaryAsync(m => m.Slug, ct);
        var boltAction = mechanisms["bolt-action"];
        var shortRecoil = mechanisms["short-recoil"];
        var longStroke = mechanisms["long-stroke-piston"];
        var shortStroke = mechanisms["short-stroke-piston"];
        var directImpingement = mechanisms["direct-impingement"];

        var tags = await db.Tags.ToDictionaryAsync(t => t.Slug, ct);
        var handgun = tags["handgun"];
        var rifle = tags["rifle"];
        var carbine = tags["carbine"];
        var assaultRifle = tags["assault-rifle"];
        var battleRifle = tags["battle-rifle"];
        var ww1 = tags["ww1"];
        var ww2 = tags["ww2"];
        var coldWar = tags["cold-war"];
        var postColdWar = tags["post-cold-war"];

        // Cartridges
        Cartridge AddCartridge(string slug, string name, string qid, CartridgeType type, Country origin, int? year = null,
            Cartridge? parent = null, params string[] aliases)
        {
            var k = new Cartridge
            {
                Slug = slug,
                Name = name,
                WikidataId = qid,
                Type = type,
                CountryOfOrigin = origin,
                DesignDate = year is { } y ? FuzzyDate.InYear(y) : null,
                ParentCartridge = parent,
                AlternativeNames = [.. aliases],
                Status = PublishStatus.Published,
            };
            db.Cartridges.Add(k);
            return k;
        }
        var nineMm = AddCartridge("9x19mm-parabellum", "9×19mm Parabellum", "Q135550", CartridgeType.Pistol, germanEmpire, 1901, null, "9mm Luger", "9mm NATO");
        var acp45 = AddCartridge("45-acp", ".45 ACP", "Q158803", CartridgeType.Pistol, usa, 1905, null, ".45 Auto", "11.43×23mm");
        var cartridge762x39 = AddCartridge("7-62x39mm", "7.62×39mm", "Q259196", CartridgeType.Intermediate, ussr, 1943, null, "7.62 Soviet", "M43");
        var cartridge545x39 = AddCartridge("5-45x39mm", "5.45×39mm", "Q238437", CartridgeType.Intermediate, ussr);
        var rem223 = AddCartridge("223-remington", ".223 Remington", "Q2076678", CartridgeType.Intermediate, usa);
        var nato556 = AddCartridge("5-56x45mm-nato", "5.56×45mm NATO", "Q238456", CartridgeType.Intermediate, usa, 1959, rem223);
        var nato762 = AddCartridge("7-62x51mm-nato", "7.62×51mm NATO", "Q259210", CartridgeType.FullPowerRifle, usa, 1947);
        AddCartridge("308-winchester", ".308 Winchester", "Q2005795", CartridgeType.FullPowerRifle, usa);
        AddCartridge("7-62x54mmr", "7.62×54mmR", "Q259199", CartridgeType.FullPowerRifle, russianEmpire);
        var springfield3006 = AddCartridge("30-06-springfield", ".30-06 Springfield", "Q158721", CartridgeType.FullPowerRifle, usa, null, null, "7.62×63mm");
        var mauser792 = AddCartridge("7-92x57mm-mauser", "7.92×57mm Mauser", "Q259287", CartridgeType.FullPowerRifle, germanEmpire, null, null, "8mm Mauser", "8×57mm IS");
        var kurz = AddCartridge("7-92x33mm-kurz", "7.92×33mm Kurz", "Q259269", CartridgeType.Intermediate, naziGermany, 1940, null, "8mm Kurz");

        // People
        Person AddPerson(string slug, string name, string qid, FuzzyDate born, FuzzyDate died, Country? nationality)
        {
            var p = new Person
            {
                Slug = slug,
                FullName = name,
                WikidataId = qid,
                BirthDate = born,
                DeathDate = died,
                Nationality = nationality,
                Status = PublishStatus.Published,
            };
            db.People.Add(p);
            return p;
        }
        var browning = AddPerson("john-moses-browning", "John Moses Browning", "Q220520", FuzzyDate.OnDay(1855, 1, 23), FuzzyDate.OnDay(1926, 11, 26), usa);
        var kalashnikov = AddPerson("mikhail-kalashnikov", "Mikhail Kalashnikov", "Q167573", FuzzyDate.OnDay(1919, 11, 10), FuzzyDate.OnDay(2013, 12, 23), ussr);
        var stoner = AddPerson("eugene-stoner", "Eugene Stoner", "Q558764", FuzzyDate.OnDay(1922, 11, 22), FuzzyDate.OnDay(1997, 4, 24), usa);
        var schmeisser = AddPerson("hugo-schmeisser", "Hugo Schmeisser", "Q60929", FuzzyDate.OnDay(1884, 9, 24), FuzzyDate.OnDay(1953, 9, 12), germany);
        var garand = AddPerson("john-garand", "John Garand", "Q1317174", FuzzyDate.OnDay(1888, 1, 1), FuzzyDate.OnDay(1974, 2, 16), usa);
        var luger = AddPerson("georg-luger", "Georg Luger", "Q112054", FuzzyDate.OnDay(1849, 3, 6), FuzzyDate.OnDay(1923, 12, 22), null);

        // Organizations
        Organization AddOrganization(string slug, string name, string qid, Country country, OrganizationType type, FuzzyDate founded)
        {
            var o = new Organization
            {
                Slug = slug,
                Name = name,
                WikidataId = qid,
                Country = country,
                Type = type,
                Founded = founded,
                Status = PublishStatus.Published,
            };
            db.Organizations.Add(o);
            return o;
        }
        var armalite = AddOrganization("armalite", "ArmaLite", "Q547140", usa, OrganizationType.Company, FuzzyDate.InYear(1954));
        var colt = AddOrganization("colts-manufacturing-company", "Colt's Manufacturing Company", "Q745019", usa, OrganizationType.Company, FuzzyDate.InYear(1855));
        var fn = AddOrganization("fn-herstal", "FN Herstal", "Q747353", belgium, OrganizationType.Company, FuzzyDate.OnDay(1889, 7, 3));
        var springfieldArmory = AddOrganization("springfield-armory", "Springfield Armory", "Q1193204", usa, OrganizationType.StateArsenal, FuzzyDate.InYear(1777));
        var mauser = AddOrganization("mauser", "Mauser", "Q166203", germany, OrganizationType.Company, FuzzyDate.InYear(1811));
        var haenel = AddOrganization("c-g-haenel", "C. G. Haenel", "Q1022482", germany, OrganizationType.Company, FuzzyDate.InYear(1840));
        var kalashnikovConcern = AddOrganization("kalashnikov-concern", "Kalashnikov Concern", "Q7427495", russia, OrganizationType.Company, FuzzyDate.InYear(1807));

        // Firearms
        Firearm AddFirearm(string slug, string name, string qid, Country origin, FuzzyDate? designed, OperatingMechanism mechanism,
            FeedSystem feed, Cartridge cartridge, int? magazine, FiringModes modes, Tag[] tags, string summary, params string[] aliases)
        {
            var f = new Firearm
            {
                Slug = slug,
                Name = name,
                WikidataId = qid,
                CountryOfOrigin = origin,
                DesignDate = designed,
                OperatingMechanism = mechanism,
                FeedSystem = feed,
                Summary = summary,
                AlternativeNames = [.. aliases],
                Tags = [.. tags],
                Status = PublishStatus.Published,
            };
            f.Variants.Add(new Variant
            {
                Slug = slug,
                Name = name,
                IsBaseVariant = true,
                MagazineCapacityStandard = magazine,
                FiringModes = modes,
                Chamberings = [new VariantCartridge { Cartridge = cartridge, IsPrimary = true }],
            });
            db.Firearms.Add(f);
            return f;
        }
        const FiringModes semiAuto = FiringModes.SemiAutomatic;
        const FiringModes selectFire = FiringModes.SemiAutomatic | FiringModes.Automatic;
        const FeedSystem boxMagazine = FeedSystem.DetachableBoxMagazine;

        var ak47 = AddFirearm("ak-47", "AK-47", "Q37116", ussr, FuzzyDate.InYear(1947), longStroke, boxMagazine, cartridge762x39, 30, selectFire,
            [assaultRifle, coldWar], "Soviet gas-operated assault rifle chambered in 7.62×39mm, designed by Mikhail Kalashnikov.",
            "AK", "Avtomat Kalashnikova", "Kalashnikov");
        var akm = AddFirearm("akm", "AKM", "Q432662", ussr, new FuzzyDate { Year = 1950, Precision = DatePrecision.Decade }, longStroke, boxMagazine, cartridge762x39, 30, selectFire,
            [assaultRifle, coldWar], "Modernized, lighter development of the AK-47.", "Avtomat Kalashnikova Modernizirovanny");
        var ak74 = AddFirearm("ak-74", "AK-74", "Q156229", ussr, FuzzyDate.InYear(1974), longStroke, boxMagazine, cartridge545x39, 30, selectFire,
            [assaultRifle, coldWar, postColdWar], "Soviet assault rifle developed from the AKM for the 5.45×39mm cartridge.");
        var sks = AddFirearm("sks", "SKS", "Q482594", ussr, FuzzyDate.InYear(1945), shortStroke, FeedSystem.InternalMagazine, cartridge762x39, 10, semiAuto,
            [carbine, coldWar], "Soviet semi-automatic carbine chambered in 7.62×39mm, designed by Sergei Simonov.",
            "Samozaryadny Karabin sistemy Simonova");
        var garandRifle = AddFirearm("m1-garand", "M1 Garand", "Q204046", usa, null, longStroke, FeedSystem.EnBlocClip, springfield3006, 8, semiAuto,
            [rifle, ww2], "American semi-automatic service rifle designed by John Garand, fed from an eight-round en bloc clip.",
            "M1 rifle", "United States Rifle, Caliber .30, M1");
        var m14 = AddFirearm("m14", "M14", "Q15082", usa, FuzzyDate.InYear(1957), shortStroke, boxMagazine, nato762, 20, selectFire,
            [battleRifle, coldWar], "American select-fire battle rifle chambered in 7.62×51mm NATO, developed from the M1 Garand.",
            "M14 rifle", "United States Rifle, 7.62 mm, M14");
        var ar10 = AddFirearm("ar-10", "AR-10", "Q676863", usa, null, directImpingement, boxMagazine, nato762, 20, selectFire,
            [battleRifle, coldWar], "Battle rifle designed by Eugene Stoner at ArmaLite.", "ArmaLite AR-10");
        var ar15 = AddFirearm("ar-15", "AR-15", "Q1073847", usa, FuzzyDate.InYear(1957), directImpingement, boxMagazine, rem223, 20, selectFire,
            [assaultRifle, coldWar], "ArmaLite's scaled-down development of the AR-10 for a small-calibre cartridge.", "ArmaLite AR-15");
        var m16 = AddFirearm("m16", "M16", "Q170318", usa, null, directImpingement, boxMagazine, nato556, null, selectFire,
            [assaultRifle, coldWar, postColdWar], "American military adaptation of the ArmaLite AR-15.", "M16 rifle");
        var m4 = AddFirearm("m4-carbine", "M4 carbine", "Q214688", usa, null, directImpingement, boxMagazine, nato556, 30, FiringModes.SemiAutomatic | FiringModes.Burst,
            [carbine, assaultRifle, postColdWar], "Shorter, lighter carbine variant of the M16 family.", "M4");
        var stg44 = AddFirearm("stg-44", "StG 44", "Q151877", naziGermany, FuzzyDate.InYear(1942), longStroke, boxMagazine, kurz, 30, selectFire,
            [assaultRifle, ww2], "German select-fire rifle chambered in 7.92×33mm Kurz, designed by Hugo Schmeisser.",
            "Sturmgewehr 44", "MP 43", "MP 44");
        var hiPower = AddFirearm("browning-hi-power", "Browning Hi-Power", "Q248874", belgium, null, shortRecoil, boxMagazine, nineMm, 13, semiAuto,
            [handgun, ww2], "Semi-automatic pistol begun by John Browning and completed by Dieudonné Saive at FN.", "P-35", "HP");
        var m1911 = AddFirearm("m1911", "M1911", "Q201419", usa, null, shortRecoil, boxMagazine, acp45, 7, semiAuto,
            [handgun, ww1, ww2], "Semi-automatic pistol designed by John Browning, chambered in .45 ACP.", "Colt M1911", "Colt Government");
        var lugerP08 = AddFirearm("luger-p08", "Luger P08", "Q154199", germanEmpire, FuzzyDate.InYear(1898), shortRecoil, boxMagazine, nineMm, 8, semiAuto,
            [handgun, ww1, ww2], "Toggle-locked semi-automatic pistol designed by Georg Luger.", "Parabellum-Pistole", "Pistole 08");
        var kar98k = AddFirearm("karabiner-98k", "Karabiner 98k", "Q60134", naziGermany, FuzzyDate.InYear(1935), boltAction, FeedSystem.InternalMagazine, mauser792, 5, FiringModes.None,
            [rifle, ww2], "German bolt-action rifle chambered in 7.92×57mm Mauser.", "K98k", "Kar98k", "Karabiner 98 kurz");

        // Designers
        void AddDesigner(Firearm f, Person? person = null, Organization? org = null, DesignerRole role = DesignerRole.LeadDesigner) =>
            f.Designers.Add(new FirearmDesigner { Person = person, Organization = org, Role = role });
        AddDesigner(ak47, kalashnikov);
        AddDesigner(akm, kalashnikov);
        AddDesigner(ak74, kalashnikov);
        AddDesigner(garandRifle, garand);
        AddDesigner(m14, org: springfieldArmory);
        AddDesigner(ar10, stoner);
        AddDesigner(ar15, stoner);
        AddDesigner(m16, stoner);
        AddDesigner(m4, org: colt);
        AddDesigner(stg44, schmeisser);
        AddDesigner(hiPower, browning);
        AddDesigner(m1911, browning);
        AddDesigner(lugerP08, luger);
        AddDesigner(kar98k, org: mauser);

        // Manufacturers
        void AddManufacturer(Firearm f, Organization org, bool licensed = false) =>
            f.Manufacturers.Add(new FirearmManufacturer { Organization = org, IsLicensed = licensed });
        AddManufacturer(ak47, kalashnikovConcern);
        AddManufacturer(akm, kalashnikovConcern);
        AddManufacturer(ak74, kalashnikovConcern);
        AddManufacturer(garandRifle, springfieldArmory);
        AddManufacturer(m14, springfieldArmory);
        AddManufacturer(ar10, armalite);
        AddManufacturer(ar15, armalite);
        AddManufacturer(ar15, colt, licensed: true);
        AddManufacturer(m16, colt);
        AddManufacturer(m4, colt);
        AddManufacturer(stg44, haenel);
        AddManufacturer(hiPower, fn);
        AddManufacturer(m1911, colt);
        AddManufacturer(m1911, springfieldArmory);
        AddManufacturer(kar98k, mauser);

        // Relations ("From <type> To")
        void Relate(Firearm from, RelationType type, Firearm to, string? note = null) =>
            db.FirearmRelations.Add(new FirearmRelation { FromFirearm = from, Type = type, ToFirearm = to, Note = note });
        Relate(akm, RelationType.DerivedFrom, ak47);
        Relate(ak74, RelationType.DerivedFrom, akm);
        Relate(m14, RelationType.DerivedFrom, garandRifle);
        Relate(ar15, RelationType.DerivedFrom, ar10);
        Relate(m16, RelationType.DerivedFrom, ar15);
        Relate(m4, RelationType.DerivedFrom, m16);
        Relate(ak47, RelationType.InfluencedBy, stg44, "Commonly asserted; the degree of influence is disputed.");
        Relate(m14, RelationType.Replaced, garandRifle);
        Relate(m16, RelationType.Replaced, m14);
        Relate(ak47, RelationType.Replaced, sks);

        await db.SaveChangesAsync(ct);
    }
}
