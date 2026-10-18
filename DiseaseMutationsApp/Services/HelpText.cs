namespace DiseaseMutationsApp.Services;

/// <summary>
/// The plain-language explanation shown when hovering (or focusing) a term in the interface. One place, so the wording
/// stays in step with <c>BIOLOGICAL_REPORT.md</c>, which holds the full description of each rule.
/// </summary>
public static class HelpText
{
    // ----- Builder form -----
    public const string Variants = "Variants to design guides for. HGVS (for example NC_000017.11:g.7674220C>T) names one exact change in a reference sequence; an rsID (for example rs334) is a database number for a known variant and is converted to HGVS.";
    public const string SpacerSize = "Length of the guide's targeting part (the spacer), in letters (18-60). Longer spacers are more specific but each window is more likely to contain other differences.";
    public const string SeedRegion = "The seed is the stretch of the spacer where a mismatch hurts binding most. Positions are counted from 0 along the spacer; the seed letters are highlighted in the tables.";
    public const string SnpPos = "Substitution rule: where the variant sits in the engineered spacer, counted from the spacer's 3' end (1 = the last letter). Default 3. The spacer is chosen among the candidates that carry the variant at this position.";
    public const string MismatchPos = "Substitution rule: which letter is deliberately changed (to A, or to U if it is A) to add one extra mismatch, counted from the spacer's 3' end (1 = the last letter). Default 4, next to the variant. It must differ from the variant position. The best position is not settled in the literature we checked; try several and compare the 'mismatches vs other allele' column.";
    public const string ShowAllAlleles = "By default an rsID is reduced to its disease-relevant allele(s) on the main gene record. Tick to design for every allele and every gene record dbSNP lists (many are not the variant you want).";

    public const string SpacerAndSeed = "Spacer size is the length of the guide's targeting part (18-60 letters). The seed is the stretch of it where a mismatch hurts binding most (positions counted from 0).";
    public const string StrandBoth = "Normal: designed from the reference strand as written (+). Complement: designed from the opposite strand.";

    // ----- Result tables -----
    public const string Spacer = "The guide's targeting RNA, written 5' to 3'. It is the reverse complement of the DNA window it targets (T becomes U). Highlighted letters: the variant (colour) and the seed region.";
    public const string GcScore = "1.0 when the GC percentage is between 40 and 60 (inclusive). Below 40 it is GC%/40; above 60 it is (100 - GC%)/40. Always between 0 and 1.";
    public const string GcPercent = "Percentage of G and C letters in the spacer. G-C pairs bind more strongly than A-U pairs.";
    public const string Homopolymers = "Number of runs of 4 or more identical letters (AAAA, CCCC, GGGG, UUUU). Such runs can cause synthesis or transcription problems; fewer is better.";
    public const string Offtargets = "How many places the guide's DNA window matches the human genome (GRCh38) with up to 2 mismatches, counted up to 6 (Bowtie). 1 = unique. 0 = no match, ranked best by design: normal for edited sequences such as deletions. This is a genome check, not a transcriptome check.";
    public const string Assembly = "Whether the DNA you would order contains cut sites that break a cloning standard: BioBrick RFC[10] (EcoRI, NotI, XbaI, SpeI, PstI) and iGEM Type IIS RFC[1000] (BsaI, SapI). Green = compatible, red = a site is present.";
    public const string Energy = "Minimum free energy of the complete gRNA (scaffold + spacer) folded by ViennaRNA, in kcal/mol. Closer to zero means a less tightly folded, more accessible guide, which ranks better.";
    public const string SpacerUnpaired = "Share of the spacer's letters left unpaired when the complete gRNA is folded by ViennaRNA. A spacer that folds on itself or sticks to the scaffold may bind its target less well; higher is more accessible.";
    public const string ScaffoldBroken = "In the fold of the complete gRNA the scaffold's own hairpin, which Cas13 recognises, is no longer formed: the spacer pairs with it. Such a guide may not load into Cas13.";
    public const string ScoreOnlyCandidate = "This is the only ordinary candidate, so a relative score would be meaningless (it would always read 100 %).";
    public const string Score = "The guide's rank turned into 0-1 (best group = 100%). Ranking order: fewest offtargets, then energy closest to zero, then GC score, then fewest homopolymers. A relative number for comparing candidates, not a prediction of activity.";
    public const string WtMismatches = "Fewest letters that differ between this guide's DNA target and the closest stretch of the OTHER allele (for a mutated-sequence guide: the normal sequence). 0-1 means the guide cannot tell the two apart; 2 or more can; more is better. Needed for a test that must react to the variant only.";
    public const string WtMismatchesLow = "Cannot reliably tell the alleles apart: it differs from the other allele by 0 or 1 letter.";
    public const string Engineered = "Built by the substitution rule: the variant sits 3rd from the spacer's 3' end and one more letter, the 4th, is deliberately changed to A or U. The idea is to make the guide more sensitive to the single-letter difference; it then has one mismatch to the mutated target and two to the normal one. Listed first; the ordinary ranking follows. (The literature source for these positions is still to be cited, see the biological report.)";
    public const string UseButton = "Use this spacer to build the complete gRNA (scaffold + spacer), copy it, fold it or add it to the shortlist.";
    public const string LociButton = "Show where in the genome this guide's DNA window aligns (chromosome, position, strand, mismatches).";
    public const string MinScore = "Hide guides whose score is below this value (0-1).";
    public const string MaxOfftargets = "Hide guides that align to the genome more than this many times.";
    public const string MinWtMismatches = "Hide guides that differ from the other allele in fewer letters than this. 2 removes guides that cannot tell the alleles apart. The engineered guide is always kept.";
    public const string GcFilter = "Keep only guides whose GC percentage lies in this range.";
    public const string Contains = "Keep only spacers that contain this sequence (letters A, C, G, U/T).";

    // ----- Tabs, strands -----
    public const string StrandNormal = "Normal: designed from the reference strand as written (+). The spacer is its reverse complement.";
    public const string StrandComplement = "Complement: designed from the opposite strand (the reverse complement of the reference).";
    public const string TargetsMrna = "This tab's spacers are complementary to the gene's mRNA, which is what Cas13 binds. Use this one for knock-down or RNA detection.";
    public const string TargetsAntisense = "This tab's spacers are NOT complementary to the gene's mRNA (they match the mRNA itself). They would not bind it; use the other strand's tab.";
    public const string OrientationUnknown = "The gene's orientation is not known for a typed HGVS. Cas13 needs a spacer complementary to the mRNA: check which strand the gene is read from.";

    // ----- Complete gRNA, shortlist, files -----
    public const string Scaffold = "The constant 36-letter handle (direct repeat) that precedes the spacer; Cas13 recognises its hairpin. Diana uses one fixed scaffold; confirm that it is the one for your Cas13 protein.";
    public const string Shortlist = "Guides you picked across variants. Send them to Pooling or download an order-ready oligo file.";
    public const string T7 = "Adds a T7 promoter to the DNA oligos so the guide can be made by in-vitro transcription.";
    public const string Pairs = "Writes the top and bottom DNA strands as separate oligos, ready to anneal.";
    public const string SaveSession = "Download your inputs, spacer/seed settings and shortlist as a file that can be loaded again here or by the grna command line.";
    public const string Permalink = "Copy a link that fills in this form. Opening it never starts a run by itself.";
    public const string LoadFile = "Load a plain list of HGVS/rsIDs, a Diana CSV report, or a saved session.";

    // ----- Disease variants page -----
    public const string DiseaseQuery = "A disease name, an OMIM number (for example 603903) or a MONDO id. Names are looked up in the Monarch Initiative knowledge graph; the variants come from NCBI ClinVar.";
    public const string Classification = "Which ClinVar germline classifications to keep. 'Pathogenic and likely pathogenic' is the usual choice for picking targets. ClinVar's classifications are shown as submitted; Diana does not re-interpret them.";
    public const string Subtypes = "Also read the variants of the disease's subtypes (up to 40). Needed for a group of diseases that has no OMIM number of its own.";
    public const string DiseaseFilter = "Keep only rows whose text (rsID, gene, variant, conditions) contains this.";
    public const string OnlyRs = "Hide rows that ClinVar gives no rsID; those can still be sent, as a genomic HGVS.";
    public const string RsOrHgvs = "The variant's rsID (a database number) or, when ClinVar has none, a genomic HGVS on GRCh38 that Diana also accepts.";
    public const string Gene = "The gene ClinVar assigns to the record. Records can be labelled with a neighbouring gene.";
    public const string ClinVarVariant = "ClinVar's own name for the variant: transcript, change in the coding sequence (c.) and in the protein (p.). Diana designs from the genomic position, not from c.";
    public const string Review = "ClinVar review status as stars (0-4): more stars mean more evidence or expert review.";

    // ----- Pooling page -----
    public const string GuideCount = "Number of guides (one per variant) to spread over wells.";
    public const string GuideList = "Paste guide names, spacers, or a Diana CSV report. One guide per line or comma-separated.";
    public const string WellCapacity = "K: the most guides that may share one tube. They are made in tandem on one RNA; long arrays can fold in ways that block Cas13 (the limit of 5 is a working assumption, not a sourced value).";
    public const string PlateFormat = "96-well (8 x 12) or 384-well (16 x 24) plate used to lay out the tubes.";
    public const string PositiveWells = "Wells that gave a signal, for example A1, B3 or Plate 2 - C4 (tube numbers like #12 also work). Diana lists the guides they implicate and flags ambiguous overlaps.";
}
