﻿using NUnit.Framework;
using ProteoformSuiteInternal;
using Proteomics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Test
{
    [TestFixture]
    public class ProteoformIdentification
    {
        [Test]
        public void assign_missing_aa_identity()
        {
            TheoreticalProteoform t = ConstructorsForTesting.make_a_theoretical("", 100087.03, 0); // sequence with all serines
            ExperimentalProteoform e = ConstructorsForTesting.ExperimentalProteoform("", 100087.03, 0, true);
            e.linked_proteoform_references = new List<Proteoform> { t };
            ExperimentalProteoform e2 = ConstructorsForTesting.ExperimentalProteoform("", 10000, 0, true);
            ConstructorsForTesting.make_relation(e, e2, ProteoformComparison.ExperimentalExperimental, 87.03);
            ModificationMotif.TryGetMotif("S", out ModificationMotif motif);
            PtmSet set = new PtmSet(new List<Ptm> { new Ptm(0, new ModificationWithMass("missing serine", new Tuple<string, string>("", ""), motif, TerminusLocalization.Any, -87.03, new Dictionary<string, IList<string>>(), new List<double>(), new List<double>(), "Missing")) });
            Sweet.lollipop.theoretical_database.possible_ptmset_dictionary[Math.Round(set.mass, 1)] = new List<PtmSet> { set };
            e.relationships.First().Accepted = true;
            e.relationships.First().peak = new DeltaMassPeak(e.relationships.First(), new HashSet<ProteoformRelation> { e.relationships.First() }); // should assign the possible ptmset
            e.identify_connected_experimentals(new List<PtmSet> { set }, new List<ModificationWithMass> { set.ptm_combination.First().modification });
            Assert.IsNotNull(e2.linked_proteoform_references);
            Assert.AreEqual(-87.03, e2.ptm_set.mass);
        }

        [Test]
        public void loss_of_ptm_set()
        {
            TheoreticalProteoform t = ConstructorsForTesting.make_a_theoretical("", 100000, 0); // sequence with all serines
            ExperimentalProteoform e = ConstructorsForTesting.ExperimentalProteoform("", 100126.03, 0, true);
            e.linked_proteoform_references = new List<Proteoform> { t };
            ModificationMotif.TryGetMotif("S", out ModificationMotif motif);
            PtmSet set = new PtmSet(new List<Ptm>
            {
                new Ptm(0, new ModificationWithMass("acetyl", new Tuple<string, string>("", ""), motif, TerminusLocalization.Any, 42.01, new Dictionary<string, IList<string>>(), new List<double>(), new List<double>(), "Mod")),
                new Ptm(0, new ModificationWithMass("acetyl", new Tuple<string, string>("", ""), motif, TerminusLocalization.Any, 42.01, new Dictionary<string, IList<string>>(), new List<double>(), new List<double>(), "Mod")),
                new Ptm(0, new ModificationWithMass("acetyl", new Tuple<string, string>("", ""), motif, TerminusLocalization.Any, 42.01, new Dictionary<string, IList<string>>(), new List<double>(), new List<double>(), "Mod")),
            });
            PtmSet set2 = new PtmSet(new List<Ptm>
            {
                new Ptm(0, new ModificationWithMass("acetyl", new Tuple<string, string>("", ""), motif, TerminusLocalization.Any, 42.01, new Dictionary<string, IList<string>>(), new List<double>(), new List<double>(), "Mod")),
                new Ptm(0, new ModificationWithMass("acetyl", new Tuple<string, string>("", ""), motif, TerminusLocalization.Any, 42.01, new Dictionary<string, IList<string>>(), new List<double>(), new List<double>(), "Mod")),
            });
            PtmSet set3 = new PtmSet(new List<Ptm>
            {
                new Ptm(0, new ModificationWithMass("acetyl", new Tuple<string, string>("", ""), motif, TerminusLocalization.Any, 42.01, new Dictionary<string, IList<string>>(), new List<double>(), new List<double>(), "Mod")),
            });
            e.ptm_set = set;
            Sweet.lollipop.theoretical_database.possible_ptmset_dictionary = new Dictionary<double, List<PtmSet>>
            {
                { Math.Round(set.mass, 1), new List<PtmSet> { set } },
                { Math.Round(set2.mass, 1), new List<PtmSet> { set2 } },
                { Math.Round(set3.mass, 1), new List<PtmSet> { set3 } },
            };
            ExperimentalProteoform e2 = ConstructorsForTesting.ExperimentalProteoform("", 10042.01, 0, true);
            ConstructorsForTesting.make_relation(e, e2, ProteoformComparison.ExperimentalExperimental, 84.02);
            e.relationships.First().Accepted = true;
            e.relationships.First().peak = new DeltaMassPeak(e.relationships.First(), new HashSet<ProteoformRelation> { e.relationships.First() });
            e.identify_connected_experimentals(new List<PtmSet> { set, set2, set3 }, new List<ModificationWithMass> { set.ptm_combination.First().modification });
            Assert.IsNotNull(e2.linked_proteoform_references);
            Assert.AreEqual(42.01, e2.ptm_set.mass);
        }
    }
}
