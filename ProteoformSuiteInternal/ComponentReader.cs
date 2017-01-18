﻿using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ProteoformSuiteInternal;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProteoformSuiteInternal
{
    public class ComponentReader
    {
        private List<Component> raw_components_in_file = new List<Component>();
        private static List<NeuCodePair> neucodePairs_in_file = new List<NeuCodePair>();

        public List<Component> read_components_from_xlsx(InputFile file, IEnumerable<Correction>correctionFactors)
        {

            this.raw_components_in_file.Clear();
            string absolute_path = file.path + "\\" + file.filename + file.extension;
            try
            {
                using (SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Open(absolute_path, false))
                {
                    // Get Data in Sheet1 of Excel file
                    IEnumerable<Sheet> sheetcollection = spreadsheetDocument.WorkbookPart.Workbook.GetFirstChild<Sheets>().Elements<Sheet>(); // Get all sheets in spread sheet document 
                    WorksheetPart worksheet_1 = (WorksheetPart)spreadsheetDocument.WorkbookPart.GetPartById(sheetcollection.First().Id.Value); // Get sheet1 Part of Spread Sheet Document
                    SheetData sheet_1 = worksheet_1.Worksheet.Elements<SheetData>().First();
                    List<Row> rowcollection = worksheet_1.Worksheet.Descendants<Row>().ToList();

                    Component new_component = new Component();
                    int charge_row_index = 0;
                    string scan_range = "";
                    for (int i = 0; i < rowcollection.Count(); i++)
                    {
                        if (i == 0) continue; //skip component header
                        
                        IEnumerable<Cell> cells = rowcollection[i].Descendants<Cell>();

                        List<string> cellStrings = new List<string>();

                        for (int k = 0; k < rowcollection[i].Descendants<Cell>().Count(); k++)
                        {
                            cellStrings.Add(GetCellValue(spreadsheetDocument, rowcollection[i].Descendants<Cell>().ElementAt(k)));
                        }
                        
                        if (cellStrings.Count > 4) //component row
                        {
                            if (i > 1) add_component(new_component); // here we're adding the previously read component
                            new_component = new Component(cellStrings, file); // starting fresh here with a newly created componet.
                            charge_row_index = 0;
                            scan_range = cellStrings[8];
                        }
                        else if (cellStrings.Count == 4) //charge-state row
                        {
                            if (charge_row_index == 0)
                            {
                                charge_row_index += 1;
                                continue; //skip charge state headers
                            }
                            else new_component.add_charge_state(cellStrings, GetCorrectionFactor(file.filename, scan_range, correctionFactors));
                        }
                    }
                    add_component(new_component); //add the final component
                }
                return remove_monoisotopic_duplicates_harmonics_from_same_scan(raw_components_in_file);
            }
            catch (IOException ex)
            {
                throw new IOException(ex.Message);
            }
        }

        private static List<Component> remove_monoisotopic_duplicates_harmonics_from_same_scan(List<Component> raw_components)
        {
            List<string> scans = raw_components.Select(c => c.scan_range).Distinct().ToList();
            List<Component> removeThese = new List<Component>();
            List<NeuCodePair> ncPairsInScan = new List<NeuCodePair>();


            foreach (string scan in scans)
            { // here
                List<Component> scanComps = raw_components.Where(c => c.scan_range == scan).OrderByDescending(i => i.intensity_sum).ToList();
                foreach (Component sc in scanComps) // this loop compresses missed monoisotopics into a single component. components are sorted by mass. in one scan, there should only be one missed mono per 
                {
                    if (removeThese.Contains(sc))
                        continue;
                    else
                    {
                        List<double> possibleMissedMonoisotopicsList = new List<double>
                    {
                        (sc.weighted_monoisotopic_mass - (3d * Lollipop.MONOISOTOPIC_UNIT_MASS)),
                        (sc.weighted_monoisotopic_mass - (2d * Lollipop.MONOISOTOPIC_UNIT_MASS)),
                        (sc.weighted_monoisotopic_mass - (1d * Lollipop.MONOISOTOPIC_UNIT_MASS)),
                        (sc.weighted_monoisotopic_mass + (0d * Lollipop.MONOISOTOPIC_UNIT_MASS)),
                        (sc.weighted_monoisotopic_mass + (1d * Lollipop.MONOISOTOPIC_UNIT_MASS)),
                        (sc.weighted_monoisotopic_mass + (2d * Lollipop.MONOISOTOPIC_UNIT_MASS)),
                        (sc.weighted_monoisotopic_mass + (3d * Lollipop.MONOISOTOPIC_UNIT_MASS)),
                    };

                        foreach (double missedMonoMass in possibleMissedMonoisotopicsList)
                        {
                            double massTolerance = missedMonoMass / 1000000d * (double)Lollipop.mass_tolerance;
                            List<Component> missedMonoisotopics = scanComps.Except(removeThese).Where(
                                cp => cp.weighted_monoisotopic_mass >= (missedMonoMass - massTolerance)
                                && cp.weighted_monoisotopic_mass <= (missedMonoMass + massTolerance)
                                ).ToList(); // this is a list of harmonics to hc

                            foreach (Component c in missedMonoisotopics.Where(m => m.id != sc.id).ToList())
                            {
                                sc.mergeTheseComponents(c);
                                removeThese.Add(c);
                            }
                        }
                    }
                }

                if (Lollipop.neucode_labeled && raw_components.FirstOrDefault().input_file.purpose == Purpose.Identification) //before we compress harmonics, we have to determine if they are neucode labeled and lysine count 14. these have special considerations
                    ncPairsInScan = find_neucode_pairs(scanComps.Except(removeThese)).ToList(); // these are not the final neucode pairs, just a temp list
                List<string> lysFourteenComponents = new List<string>();
                foreach (NeuCodePair ncp in ncPairsInScan)
                {
                    if (ncp.lysine_count == 14)
                    {
                        lysFourteenComponents.Add(ncp.neuCodeLight.id);
                        lysFourteenComponents.Add(ncp.neuCodeHeavy.id);
                    }
                }

                List<Component> someComponents = scanComps.OrderByDescending(w => w.weighted_monoisotopic_mass).ToList();
                foreach (Component hc in someComponents)
                {
                    if (removeThese.Contains(hc))
                        continue;

                    List<double> possibleHarmonicList = new List<double> // 2 missed on the top means up to 4 missed monos on the 2nd harmonic and 6 missed monos on the 3rd harmonic
                    {
                        (hc.weighted_monoisotopic_mass - (4d * Lollipop.MONOISOTOPIC_UNIT_MASS))/2d,
                        (hc.weighted_monoisotopic_mass - (3d * Lollipop.MONOISOTOPIC_UNIT_MASS))/2d,
                        (hc.weighted_monoisotopic_mass - (2d * Lollipop.MONOISOTOPIC_UNIT_MASS))/2d,
                        (hc.weighted_monoisotopic_mass - (1d * Lollipop.MONOISOTOPIC_UNIT_MASS))/2d,
                        (hc.weighted_monoisotopic_mass - (0d * Lollipop.MONOISOTOPIC_UNIT_MASS))/2d,
                        (hc.weighted_monoisotopic_mass + (1d * Lollipop.MONOISOTOPIC_UNIT_MASS))/2d,
                        (hc.weighted_monoisotopic_mass + (2d * Lollipop.MONOISOTOPIC_UNIT_MASS))/2d,
                        (hc.weighted_monoisotopic_mass + (3d * Lollipop.MONOISOTOPIC_UNIT_MASS))/2d,
                        (hc.weighted_monoisotopic_mass + (4d * Lollipop.MONOISOTOPIC_UNIT_MASS))/2d,
                        (hc.weighted_monoisotopic_mass - (6d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass - (5d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass - (4d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass - (3d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass - (2d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass - (1d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass - (0d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass + (1d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass + (2d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass + (3d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass + (4d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass + (5d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                        (hc.weighted_monoisotopic_mass + (6d * Lollipop.MONOISOTOPIC_UNIT_MASS))/3d,
                    };

                    foreach (double harmonicMass in possibleHarmonicList)
                    {
                        double massTolerance = harmonicMass / 1000000d * (double)Lollipop.mass_tolerance;
                        List<Component> harmonics = scanComps.Except(removeThese).Where(
                            cp => cp.weighted_monoisotopic_mass >= (harmonicMass - massTolerance)
                            && cp.weighted_monoisotopic_mass <= (harmonicMass + massTolerance)
                            ).ToList(); // this is a list of harmonics to hc
                        List<Component> someHarmonics = harmonics.Where(harmonicComponent => harmonicComponent.id != hc.id).ToList();
                        foreach (Component h in someHarmonics) // now that we have a list of harmonics to hc, we have to figure out what to do with them
                        {
                            if (removeThese.Contains(h) || removeThese.Contains(hc))
                                continue;
                            int parCsCount = hc.charge_states.Count();
                            int cldCsCount = h.charge_states.Count();
                            double parMass = hc.weighted_monoisotopic_mass;
                            double cldMass = h.weighted_monoisotopic_mass;
                            bool fourteen = lysFourteenComponents.Contains(h.id);

                            if (lysFourteenComponents.Contains(h.id))
                            {
                                //string line = "BEFORE: \t" + hc.scan_range + "\t" + hc.id + "\t" + h.id + "\t" + hc.weighted_monoisotopic_mass + "\t" + h.weighted_monoisotopic_mass + "\t" + hc.num_charge_states + "\t" + h.num_charge_states + "\t" + hc.intensity_sum + "\t" + h.intensity_sum + "\t" + "lysine count 14" + "\t" + hc.input_file.filename;
                                //File.AppendAllText(@"C:\Users\Michael\Downloads\new_h_m.txt", line + Environment.NewLine);

                                h.mergeTheseComponents(hc);
                                removeThese.Add(hc);

                                //line = "***AFTER: \t" + h.scan_range + "\t" + h.id + "\t" + h.weighted_monoisotopic_mass + "\t" + h.num_charge_states + "\t" + h.intensity_sum + "\t" + "lysine count 14" + "\t" + h.input_file.filename;
                                //File.AppendAllText(@"C:\Users\Michael\Downloads\new_h_m.txt", line + Environment.NewLine);
                            }
                            else
                            {
                                if (hc.charge_states.Count() >= 4 && h.charge_states.Count() >= 4)
                                    continue;
                                if (hc.charge_states.Count() == h.charge_states.Count())
                                {
                                    //string line = "BEFORE: \t" + h.scan_range + "\t" + h.id + "\t" + hc.id + "\t" + h.weighted_monoisotopic_mass + "\t" + hc.weighted_monoisotopic_mass + "\t" + h.num_charge_states + "\t" + hc.num_charge_states + "\t" + h.intensity_sum + "\t" + hc.intensity_sum + "\t" + hc.input_file.filename;

                                    //throw out the lower mass component
                                    if (hc.weighted_monoisotopic_mass > h.weighted_monoisotopic_mass)
                                    {
                                        //line = line + "\t" + "equal # CS throw out lower mass";
                                        //File.AppendAllText(@"C:\Users\Michael\Downloads\new_h_m.txt", line + Environment.NewLine);

                                        hc.mergeTheseComponents(h);
                                        removeThese.Add(h);

                                        //line = "***AFTER: \t" + hc.scan_range + "\t" + hc.id + "\t" + hc.weighted_monoisotopic_mass + "\t" + hc.num_charge_states + "\t" + hc.intensity_sum + "\t" + hc.input_file.filename;
                                        //File.AppendAllText(@"C:\Users\Michael\Downloads\new_h_m.txt", line + Environment.NewLine);
                                    }
                                    else
                                    {
                                        //line = line + "\t" + "equal # CS throw out lower mass";
                                        //File.AppendAllText(@"C:\Users\Michael\Downloads\new_h_m.txt", line + Environment.NewLine);

                                        h.mergeTheseComponents(hc);
                                        removeThese.Add(hc);

                                        //line = "***AFTER: \t" + h.scan_range + "\t" + h.id + "\t" + h.weighted_monoisotopic_mass + "\t" + h.num_charge_states + "\t" + h.intensity_sum + "\t" + h.input_file.filename;
                                        //File.AppendAllText(@"C:\Users\Michael\Downloads\new_h_m.txt", line + Environment.NewLine);
                                    }
                                }
                                else
                                {
                                    //string line = "BEFORE: \t" + h.scan_range + "\t" + h.id + "\t" + hc.id + "\t" + h.weighted_monoisotopic_mass + "\t" + hc.weighted_monoisotopic_mass + "\t" + h.num_charge_states + "\t" + hc.num_charge_states + "\t" + h.intensity_sum + "\t" + hc.intensity_sum + "\t" + hc.input_file.filename;
                                    //throw out the component with fewer charge states
                                    if (hc.charge_states.Count() > h.charge_states.Count())
                                    {
                                        //line = line + "\t" + "unequal # CS throw out fewer CS";
                                        //File.AppendAllText(@"C:\Users\Michael\Downloads\new_h_m.txt", line + Environment.NewLine);

                                        hc.mergeTheseComponents(h);
                                        removeThese.Add(h);

                                        //line = "***AFTER: \t" + hc.scan_range + "\t" + hc.id + "\t" + hc.weighted_monoisotopic_mass + "\t" + hc.num_charge_states + "\t" + hc.intensity_sum + "\t" + hc.input_file.filename;
                                        //File.AppendAllText(@"C:\Users\Michael\Downloads\new_h_m.txt", line + Environment.NewLine);
                                    }
                                    else
                                    {
                                        //line = line + "\t" + "unequal # CS throw out fewer CS";
                                        //File.AppendAllText(@"C:\Users\Michael\Downloads\new_h_m.txt", line + Environment.NewLine);

                                        h.mergeTheseComponents(hc);
                                        removeThese.Add(hc);

                                        //line = "***AFTER: \t" + h.scan_range + "\t" + h.id + "\t" + h.weighted_monoisotopic_mass + "\t" + h.num_charge_states + "\t" + h.intensity_sum + "\t" + h.input_file.filename;
                                        //File.AppendAllText(@"C:\Users\Michael\Downloads\new_h_m.txt", line + Environment.NewLine);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return raw_components.Except(removeThese).ToList();
        }

        public static List<NeuCodePair> find_neucode_pairs(IEnumerable<Component> components_in_file_scanrange)
        {
            List<NeuCodePair> pairsInScanRange = new List<NeuCodePair>();
            //Add putative neucode pairs. Must be in same spectrum, mass must be within 6 Da of each other
            List<Component> components = components_in_file_scanrange.OrderBy(c => c.weighted_monoisotopic_mass).ToList();
            foreach (Component lower_component in components)
            {
                IEnumerable<Component> higher_mass_components = components.Where(higher_component => higher_component != lower_component && higher_component.weighted_monoisotopic_mass > lower_component.weighted_monoisotopic_mass);
                foreach (Component higher_component in higher_mass_components)
                {
                    double mass_difference = higher_component.weighted_monoisotopic_mass - lower_component.weighted_monoisotopic_mass;
                    if (mass_difference < 6)
                    {
                        List<int> lower_charges = lower_component.charge_states.Select(charge_state => charge_state.charge_count).ToList<int>();
                        List<int> higher_charges = higher_component.charge_states.Select(charge_states => charge_states.charge_count).ToList<int>();
                        List<int> overlapping_charge_states = lower_charges.Intersect(higher_charges).ToList();                   
                        double lower_intensity = lower_component.calculate_sum_intensity_olcs(overlapping_charge_states);
                        double higher_intensity = higher_component.calculate_sum_intensity_olcs(overlapping_charge_states);
                        bool light_is_lower = true; //calculation different depending on if neucode light is the heavier/lighter component
                        if (lower_intensity > 0 && higher_intensity > 0)
                        {
                            NeuCodePair pair;
                            if (lower_intensity > higher_intensity)
                                pair = new NeuCodePair(lower_component, higher_component, mass_difference, overlapping_charge_states, light_is_lower); //lower mass is neucode light
                            else
                                pair = new NeuCodePair(higher_component, lower_component, mass_difference, overlapping_charge_states, !light_is_lower); //higher mass is neucode light
                            if ((pair.weighted_monoisotopic_mass <= (pair.neuCodeHeavy.weighted_monoisotopic_mass + Lollipop.MONOISOTOPIC_UNIT_MASS)) // the heavy should be at higher mass. Max allowed is 1 dalton less than light.                                    
                                && !neucodePairs_in_file.Any(p => p.id_heavy == pair.id_light && p.neuCodeLight.intensity_sum > pair.neuCodeLight.intensity_sum)) // we found that any component previously used as a heavy, which has higher intensity is probably correct and that that component should not get reuused as a light.
                                pairsInScanRange.Add(pair);
                        }
                    }
                }
            }
            return pairsInScanRange;
        }

        private static string GetCellValue(SpreadsheetDocument document, Cell cell)
        {
            SharedStringTablePart stringTablePart = document.WorkbookPart.SharedStringTablePart;
            string value = cell.CellValue.InnerXml;
            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString && value != null)
                return stringTablePart.SharedStringTable.ChildElements[Int32.Parse(value)].InnerText;
            else
                return value;
        }


        public double GetCorrectionFactor(string filename, string scan_range, IEnumerable<Correction> correctionFactors)
        {
            if(correctionFactors == null) return 0D;

            int[] scans = new int[2] { 0, 0 };
            try
            {
                scans = Array.ConvertAll<string, int>(scan_range.Split('-').ToArray(), int.Parse);
            }
            catch
            { }

            if (scans[0] <= 0 || scans[1] <= 0) return 0D;

            IEnumerable<double> allCorrectionFactors = 
                (from s in correctionFactors
                 where s.file_name == filename
                 where s.scan_number >= scans[0]
                where s.scan_number <= scans[1]
                select s.correction).ToList();

            if (allCorrectionFactors.Count() <= 0) return 0D;

            return allCorrectionFactors.Average();                
        }

        //Reading in Top-down excel
        public static List<Psm> ReadTDFile(string filename, TDProgram td_program)
        {
            List<Psm> psm_list = new List<Psm>();
            using (SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Open(filename, false))
            {
                // Get Data in Sheet1 of Excel file
                IEnumerable<Sheet> sheetcollection = spreadsheetDocument.WorkbookPart.Workbook.GetFirstChild<Sheets>().Elements<Sheet>(); // Get all sheets in spread sheet document 
                WorksheetPart worksheet_1 = (WorksheetPart)spreadsheetDocument.WorkbookPart.GetPartById(sheetcollection.First().Id.Value); // Get sheet1 Part of Spread Sheet Document
                SheetData sheet_1 = worksheet_1.Worksheet.Elements<SheetData>().First();
                List<Row> rowcollection = worksheet_1.Worksheet.Descendants<Row>().ToList();
                for (int i = 1; i < rowcollection.Count; i++)   //skip first row (headers)
                {
                    List<string> cellStrings = new List<string>();
                    for (int k = 0; k < rowcollection[i].Descendants<Cell>().Count(); k++)
                    {
                        cellStrings.Add(GetCellValue(spreadsheetDocument, rowcollection[i].Descendants<Cell>().ElementAt(k)));
                    }

                    if (td_program == TDProgram.NRTDP)
                    {
                        Psm new_psm = new Psm(cellStrings[8] + cellStrings[4], filename, Convert.ToInt16(cellStrings[5]), Convert.ToInt16(cellStrings[6]),
                            0, 0, Convert.ToDouble(cellStrings[14]), 0, cellStrings[2], Convert.ToDouble(cellStrings[12]), 0, 0, PsmType.TopDown);
                        psm_list.Add(new_psm);
                    }

                    else if (td_program == TDProgram.ProSight)
                    {
                        Psm new_psm = new Psm(cellStrings[6] + cellStrings[8], cellStrings[14], 0, 0, 0, 0, Convert.ToDouble(cellStrings[20]), 0, cellStrings[4], Convert.ToDouble(cellStrings[9]), 0, Convert.ToDouble(cellStrings[11]), PsmType.TopDown);
                        psm_list.Add(new_psm);
                    }
                }
            }
            return psm_list;
        }

        private void add_component(Component c)
        {
            //c.calculate_sum_intensity();
            //c.calculate_weighted_monoisotopic_mass();
            this.raw_components_in_file.Add(c);
        }
    }
}
