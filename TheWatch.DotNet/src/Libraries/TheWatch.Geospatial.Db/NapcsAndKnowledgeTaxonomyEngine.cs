using System.Collections.Concurrent;
using TheWatch.Contracts;
using static TheWatch.Contracts.LibraryOfCongressTaxonomyContracts;
using static TheWatch.Contracts.NapcsProductContracts;

namespace TheWatch.Geospatial.Db;

public interface INapcsAndKnowledgeTaxonomyEngine
{
    void RegisterNapcsProduct(NapcsProductClassification product);
    void RegisterLccClassification(LccClassification lcc);
    void RegisterSubjectHeading(LcSubjectHeading heading);
    void RegisterLexeme(LexemeKnowledgeAnchor lexeme);
    IReadOnlyList<NapcsProductClassification> GetProductsForNaics(string naicsCode);
    IReadOnlyList<LexemeKnowledgeAnchor> QueryLexemesByConcept(string conceptQuery);
    IReadOnlyList<LcSubjectHeading> ResolveSubjectHeadings(string term);
}

public sealed class NapcsAndKnowledgeTaxonomyEngine : INapcsAndKnowledgeTaxonomyEngine
{
    private readonly ConcurrentDictionary<string, NapcsProductClassification> _napcsProducts = new();
    private readonly ConcurrentDictionary<string, LccClassification> _lccClassifications = new();
    private readonly ConcurrentDictionary<string, LcSubjectHeading> _subjectHeadings = new();
    private readonly ConcurrentDictionary<string, LexemeKnowledgeAnchor> _lexemes = new();

    public NapcsAndKnowledgeTaxonomyEngine()
    {
        SeedStandardKnowledgeTaxonomies();
    }

    private void SeedStandardKnowledgeTaxonomies()
    {
        // Seed NAPCS Products
        var napcs = new List<NapcsProductClassification>
        {
            new("621910.01", "Advanced Life Support Emergency Transport", "Emergency paramedic response, onboard defibrillation, patient stabilization", "621910", "EmergencyService", true),
            new("922160.01", "Structural Fire Suppression and Hazardous Containment", "Fire attack, structural ventilation, HAZMAT primary neutralization", "922160", "EmergencyService", true),
            new("488190.01", "Autonomous AED Air Courier Flight Delivery", "Rapid drone transit and delivery of Automated External Defibrillators", "488190", "TacticalLogistics", true),
            new("423450.01", "Automated External Defibrillator (AED) Hardware Unit", "Class III FDA cleared automated external defibrillator unit", "423450", "MedicalDevice", true),
            new("517111.01", "Mission-Critical FirstNet Tactical Broadband Data Stream", "Priority QoS emergency wireless and fiber data backhaul", "517111", "BroadbandData", true)
        };

        foreach (var p in napcs)
        {
            _napcsProducts.TryAdd(p.NapcsCode, p);
        }

        // Seed Library of Congress Classifications
        var lccs = new List<LccClassification>
        {
            new("RA645.5", "Emergency Medical Services and Systems", LccMainClass.RA_PublicAspectsOfMedicineEmergencyMedical, "Covers pre-hospital emergency care, trauma registries, and ambulance dispatch", new List<string> { "Emergency medical services", "Trauma centers", "Triage" }),
            new("TH9111", "Fire Protection and Prevention Engineering", LccMainClass.TH_BuildingConstructionFirePrevention, "Covers fire tactics, thermal behavior, and extinguishing agents", new List<string> { "Fire extinction", "Fire prevention", "Smoke control" }),
            new("HV553", "Emergency Management and Disaster Relief Operations", LccMainClass.HV_SocialPathologyEmergencyManagement, "Covers national incident management systems (NIMS), evacuation coordination", new List<string> { "Disaster relief", "Civil defense", "Evacuation" }),
            new("TL589", "Aeronautical Instruments and Autonomous Flight Systems", LccMainClass.TL_MotorVehiclesAeronauticsUav, "Covers UAV telemetry, GPS navigation, and payload drop mechanisms", new List<string> { "Unmanned aerial vehicles", "Aeronautical navigation" })
        };

        foreach (var l in lccs)
        {
            _lccClassifications.TryAdd(l.LccCallNumber, l);
        }

        // Seed LCSH Subject Headings
        var headings = new List<LcSubjectHeading>
        {
            new("sh85042753", "Emergency medical services", new List<string> { "EMS", "Ambulance service", "Paramedic rescue" }, new List<string> { "Medical care" }, new List<string> { "Triage", "Trauma centers" }, new List<string> { "621910", "622110" }),
            new("sh85048520", "Fire extinction", new List<string> { "Firefighting", "Fire suppression" }, new List<string> { "Fire protection" }, new List<string> { "Structural fires", "Wildland fires" }, new List<string> { "922160" }),
            new("sh85038318", "Disaster relief", new List<string> { "Emergency management", "Humanitarian response" }, new List<string> { "Civil defense" }, new List<string> { "Evacuation of civilians", "Search and rescue operations" }, new List<string> { "922120", "922160" })
        };

        foreach (var h in headings)
        {
            _subjectHeadings.TryAdd(h.HeadingId, h);
        }

        // Seed Lexemes
        var lexemes = new List<LexemeKnowledgeAnchor>
        {
            new("LEX-TRIAGE-01", "triage", "noun", "The sorting of and allocation of treatment to patients and casualties according to priority.", "From French 'trier' (to sort, cull), 18th century military medicine.", "RA645.5", "621910", "Oxford English Dictionary & Military Medical Archives"),
            new("LEX-AED-01", "defibrillator", "noun", "An apparatus used to control heart fibrillation by application of an electric current to the chest wall.", "From de- + fibrillation + -or, mid-20th century.", "RA645.5", "423450", "Medical Lexeme Corpus"),
            new("LEX-GEOFENCE-01", "geofence", "noun", "A virtual geographic boundary defined by GPS or RFID technology.", "Compound geo- (earth) + fence (enclosure), late 20th century.", "TL589", "541512", "Geospatial Computing Lexicon")
        };

        foreach (var lex in lexemes)
        {
            _lexemes.TryAdd(lex.LexemeId, lex);
        }
    }

    public void RegisterNapcsProduct(NapcsProductClassification product)
    {
        _napcsProducts[product.NapcsCode] = product;
    }

    public void RegisterLccClassification(LccClassification lcc)
    {
        _lccClassifications[lcc.LccCallNumber] = lcc;
    }

    public void RegisterSubjectHeading(LcSubjectHeading heading)
    {
        _subjectHeadings[heading.HeadingId] = heading;
    }

    public void RegisterLexeme(LexemeKnowledgeAnchor lexeme)
    {
        _lexemes[lexeme.LexemeId] = lexeme;
    }

    public IReadOnlyList<NapcsProductClassification> GetProductsForNaics(string naicsCode)
    {
        return _napcsProducts.Values.Where(p => p.CorrespondingNaicsCode == naicsCode).ToList();
    }

    public IReadOnlyList<LexemeKnowledgeAnchor> QueryLexemesByConcept(string conceptQuery)
    {
        var q = conceptQuery.ToLowerInvariant();
        return _lexemes.Values
            .Where(l => l.Lemma.ToLowerInvariant().Contains(q) || l.Definition.ToLowerInvariant().Contains(q))
            .ToList();
    }

    public IReadOnlyList<LcSubjectHeading> ResolveSubjectHeadings(string term)
    {
        var t = term.ToLowerInvariant();
        return _subjectHeadings.Values
            .Where(h => h.PreferredTerm.ToLowerInvariant().Contains(t) || h.VariantTerms.Any(v => v.ToLowerInvariant().Contains(t)))
            .ToList();
    }
}
