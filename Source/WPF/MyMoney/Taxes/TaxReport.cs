﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Walkabout.Reports;
using Walkabout.Data;
using System.Windows;
using Walkabout.Interfaces.Reports;
using Walkabout.Migrate;
using Microsoft.Win32;
using Walkabout.Controls;
using System.Windows.Controls;
using Walkabout.Utilities;
using Walkabout.Views;
using System.Windows.Documents;
using System.Diagnostics;

namespace Walkabout.Taxes
{

    //=========================================================================================
    public class TaxReport : Report
    {
        FlowDocumentView view;
        MyMoney money;
        int year;
        bool consolidateOnDateSold;
        bool capitalGainsOnly;

        public TaxReport(FlowDocumentView view, MyMoney money)
        {
            this.view = view;
            this.year = DateTime.Now.Year;
            this.money = money;
        }

        public override void Generate(IReportWriter writer)
        {
            FlowDocumentReportWriter fwriter = (FlowDocumentReportWriter)writer;
            writer.WriteHeading("Tax Report For ");

            int startYear = year;
            int lastYear = year;

            ICollection<Transaction> transactions = this.money.Transactions.GetAllTransactionsByDate();
            Transaction first = transactions.FirstOrDefault();
            if (first != null)
            {
                startYear = first.Date.Year;
            }
            Transaction last = transactions.LastOrDefault();
            if (last != null)
            {
                lastYear = last.Date.Year;
            }
            Paragraph heading = fwriter.CurrentParagraph;

            ComboBox byYearCombo = new ComboBox();
            byYearCombo.Margin = new System.Windows.Thickness(5, 0, 0, 0);
            int selected = -1;
            for (int i = startYear; i <= lastYear; i++)
            {
                if (i == this.year)
                {
                    selected = i;
                }
                byYearCombo.Items.Add(i);
            }

            byYearCombo.SelectedItem = selected != -1 ? selected : lastYear;
            byYearCombo.SelectionChanged += OnYearChanged;
            byYearCombo.Margin = new Thickness(10, 0, 0, 0);

            heading.Inlines.Add(new InlineUIContainer(byYearCombo));

            /*
            <StackPanel Margin="10,5,10,5"  Grid.Row="2" Orientation="Horizontal">
                <TextBlock Text="Consolidate securities by: " Background="Transparent"/>
                <ComboBox x:Name="ConsolidateSecuritiesCombo" SelectedIndex="0">
                    <ComboBoxItem>Date Acquired</ComboBoxItem>
                    <ComboBoxItem>Date Sold</ComboBoxItem>
                </ComboBox>
            </StackPanel>
            <CheckBox Margin="10,5,10,5" x:Name="CapitalGainsOnlyCheckBox" Grid.Row="3">Capital Gains Only</CheckBox>
            */

            ComboBox consolidateCombo = new ComboBox();
            consolidateCombo.Items.Add("Date Acquired");
            consolidateCombo.Items.Add("Date Sold");
            consolidateCombo.SelectedIndex = this.consolidateOnDateSold ? 1 : 0;
            consolidateCombo.SelectionChanged += OnConsolidateComboSelectionChanged;

            writer.WriteParagraph("Consolidate securities by: ");
            Paragraph prompt = fwriter.CurrentParagraph;
            prompt.Margin = new Thickness(0, 0, 0, 4);
            prompt.Inlines.Add(new InlineUIContainer(consolidateCombo));

            CheckBox checkBox = new CheckBox();
            checkBox.Content = "Capital Gains Only";
            checkBox.IsChecked = this.capitalGainsOnly;
            checkBox.Checked += OnCapitalGainsOnlyChanged;
            checkBox.Unchecked += OnCapitalGainsOnlyChanged;
            writer.WriteParagraph("");
            Paragraph checkBoxParagraph = fwriter.CurrentParagraph;
            checkBoxParagraph.Inlines.Add(new InlineUIContainer(checkBox));

            if (!capitalGainsOnly)
            {
                // find all tax related categories and summarize accordingly.
                GenerateCategories(writer);
            }
            GenerateCapitalGains(writer);

            FlowDocument document = view.DocumentViewer.Document;
            document.Blocks.InsertAfter(document.Blocks.FirstBlock, new BlockUIContainer(CreateExportTxfButton()));
        }

        void WriteHeaders(IReportWriter writer)
        {
            writer.StartTable();
            writer.StartColumnDefinitions();
            for (int i = 0; i < 9; i++)
            {
                writer.WriteColumnDefinition("Auto", 100, double.MaxValue);
            }
            writer.EndColumnDefinitions();

            writer.StartHeaderRow();
            writer.StartCell();
            writer.WriteParagraph("Security");
            writer.EndCell();
            writer.StartCell();
            writer.WriteNumber("Quantity");
            writer.EndCell();
            writer.StartCell();
            writer.WriteNumber("Date Acquired");
            writer.EndCell();
            writer.StartCell();
            writer.WriteNumber("Acquisition Price");
            writer.EndCell();
            writer.StartCell();
            writer.WriteNumber("Cost Basis");
            writer.EndCell();
            writer.StartCell();
            writer.WriteNumber("Date Sold");
            writer.EndCell();
            writer.StartCell();
            writer.WriteNumber("Sale Price");
            writer.EndCell();
            writer.StartCell();
            writer.WriteNumber("Proceeds");
            writer.EndCell();
            writer.StartCell();
            writer.WriteNumber("Gain or Loss");
            writer.EndCell();
            writer.EndRow();
        }

        private void OnCapitalGainsOnlyChanged(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = (CheckBox)sender;
            this.capitalGainsOnly = checkBox.IsChecked == true;
            view.Generate(this);
        }

        private void OnConsolidateComboSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox box = (ComboBox)sender;
            int index = (int)box.SelectedIndex;
            this.consolidateOnDateSold = index == 1;
            view.Generate(this);
        }

        private void OnYearChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox box = (ComboBox)sender;
            this.year = (int)box.SelectedItem;
            view.Generate(this);
        }

        private decimal GetSalesTax()
        {
            decimal total = 0;

            foreach (Transaction t in money.Transactions)
            {
                if (t.Date.Year == this.year && !t.IsDeleted && t.Status != TransactionStatus.Void)
                {
                    total += t.NetSalesTax;
                }
            }

            return total;
        }

        private void GenerateCapitalGains(IReportWriter writer)
        {
            DateTime toDate = new DateTime(year + 1, 1, 1);
            var calculator = new CapitalGainsTaxCalculator(this.money, toDate, this.consolidateOnDateSold, true);

            List<SecuritySale> errors = new List<SecuritySale>(from s in calculator.GetSales() where s.Error != null select s);

            if (errors.Count > 0) 
            {
                writer.WriteHeading("Errors Found");
                foreach (SecuritySale error in errors) 
                {
                    writer.WriteParagraph(error.Error.Message);
                }
            }

            if ((from u in calculator.Unknown where u.DateSold.Year == this.year select u).Any())
            {
                writer.WriteHeading("Capital Gains with Unknown Cost Basis");

                writer.StartTable();
                writer.StartColumnDefinitions();
                for(int i = 0; i < 4; i++)
                {
                    writer.WriteColumnDefinition("Auto", 100, double.MaxValue);
                }
                writer.EndColumnDefinitions();

                writer.StartHeaderRow();
                writer.StartCell();
                writer.WriteParagraph("Security");
                writer.EndCell();
                writer.StartCell();
                writer.WriteNumber("Quantity");
                writer.EndCell();
                writer.StartCell();
                writer.WriteNumber("Date Sold");
                writer.EndCell();
                writer.StartCell();
                writer.WriteNumber("Sale Price");
                writer.EndCell();
                writer.StartCell();
                writer.WriteNumber("Proceeds");
                writer.EndCell();
                writer.EndRow();

                foreach (var data in calculator.Unknown)
                {
                    if (data.DateSold.Year != this.year) continue;
                    writer.StartRow();
                    writer.StartCell();
                    writer.WriteParagraph(data.Security.Name);
                    writer.EndCell();

                    writer.StartCell();
                    writer.WriteNumber(Rounded(data.UnitsSold, 3));
                    writer.EndCell();

                    writer.StartCell();
                    writer.WriteNumber(data.DateSold.ToShortDateString());
                    writer.EndCell();

                    writer.StartCell();
                    writer.WriteNumber(data.SalePricePerUnit.ToString("C"));
                    writer.EndCell();

                    writer.StartCell();
                    writer.WriteNumber(data.SaleProceeds.ToString("C"));
                    writer.EndCell();
                }

                writer.EndTable();
            }

            if (calculator.ShortTerm.Count > 0)
            {
                decimal total = 0;
                writer.WriteHeading("Short Term Capital Gains and Losses");
                WriteHeaders(writer);
                foreach (var data in calculator.ShortTerm)
                {
                    if (data.DateSold.Year != this.year) continue;
                    WriteCapitalGains(writer, data);
                    total += data.TotalGain;
                }
                WriteCapitalGainsTotal(writer, total);
                writer.EndTable();
            }

            if (calculator.LongTerm.Count > 0)
            {
                decimal total = 0;
                writer.WriteHeading("Long Term Capital Gains and Losses");
                WriteHeaders(writer);
                foreach (var data in calculator.LongTerm)
                {
                    if (data.DateSold.Year != this.year) continue;
                    WriteCapitalGains(writer, data);
                    total += data.TotalGain;
                }
                WriteCapitalGainsTotal(writer, total);
            }
            writer.EndTable();
        }

        private void WriteCapitalGainsTotal(IReportWriter writer, decimal total)
        {
            writer.StartHeaderRow();
            writer.StartCell();
            writer.WriteParagraph("Total");
            writer.EndCell();

            writer.StartCell();
            writer.EndCell();

            writer.StartCell();
            writer.EndCell();

            writer.StartCell();
            writer.EndCell();

            writer.StartCell();
            writer.EndCell();

            writer.StartCell();
            writer.EndCell();

            writer.StartCell();
            writer.EndCell();

            writer.StartCell();
            writer.EndCell();

            writer.StartCell();
            writer.WriteNumber(GiveUpTheFractionalPennies(total).ToString("C"));
            writer.EndCell();

            writer.EndRow();
        }

        void WriteCapitalGains(IReportWriter writer, SecuritySale data)
        {

            writer.StartRow();
            writer.StartCell();
            writer.WriteParagraph(data.Security.Name);
            writer.EndCell();

            writer.StartCell();
            writer.WriteNumber(Rounded(data.UnitsSold, 3));
            writer.EndCell();

            writer.StartCell();
            if (data.DateAcquired == null)
            {
                writer.WriteNumber("VARIOUS");
            }
            else
            {
                writer.WriteNumber(data.DateAcquired.Value.ToShortDateString());
            }
            writer.EndCell();

            writer.StartCell();
            writer.WriteNumber(data.CostBasisPerUnit.ToString("C"));
            writer.EndCell();

            writer.StartCell();
            writer.WriteNumber(data.TotalCostBasis.ToString("C"));
            writer.EndCell();

            writer.StartCell();
            writer.WriteNumber(data.DateSold.ToShortDateString());
            writer.EndCell();

            writer.StartCell();
            writer.WriteNumber(data.SalePricePerUnit.ToString("C"));
            writer.EndCell();

            writer.StartCell();
            writer.WriteNumber(data.SaleProceeds.ToString("C"));
            writer.EndCell();

            writer.StartCell();
            writer.WriteNumber(GiveUpTheFractionalPennies(data.TotalGain).ToString("C"));
            writer.EndCell();

            writer.EndRow();

        }

        private void GenerateCategories(IReportWriter writer)
        {
            TaxCategoryCollection taxCategories = new TaxCategoryCollection();
            List<TaxCategory> list = taxCategories.GenerateGroups(money, year);

            if (list == null)
            {
                writer.WriteParagraph("You have not associated any categories with tax categories.");
                writer.WriteParagraph("Please use the Category Properties dialog to associate tax categories then try again.");
                return;
            }

            writer.WriteHeading("Tax Categories");
            writer.StartTable();

            writer.StartColumnDefinitions();
            writer.WriteColumnDefinition("auto", 100, double.MaxValue);
            writer.WriteColumnDefinition("auto", 100, double.MaxValue);
            writer.WriteColumnDefinition("auto", 100, double.MaxValue);
            writer.EndColumnDefinitions();
            writer.StartHeaderRow();
            writer.StartCell();
            writer.WriteParagraph("Category");
            writer.EndCell();
            writer.StartCell();
            writer.WriteNumber("Amount");
            writer.EndCell();
            writer.StartCell();
            writer.WriteNumber("Tax Excempt");
            writer.EndCell();
            writer.EndRow();

            decimal tax = GetSalesTax();

            writer.StartRow();
            writer.StartCell();
            writer.WriteParagraph("Sales Tax");
            writer.EndCell();
            writer.StartCell();
            writer.WriteNumber(tax.ToString("C"), FontStyles.Normal, FontWeights.Bold, null);
            writer.EndCell();
            writer.EndRow();            

            foreach (TaxCategory tc in list)
            {
                writer.StartHeaderRow();
                writer.StartCell();
                writer.WriteParagraph(tc.Name);
                writer.EndCell();
                writer.StartCell();
                writer.EndCell();
                writer.EndRow();

                decimal sum = 0;
                IDictionary<string, List<Transaction>> groups = tc.Groups;
                foreach (KeyValuePair<string, List<Transaction>> subtotal in groups)
                {
                    writer.StartRow();
                    writer.StartCell();
                    writer.WriteParagraph(subtotal.Key);
                    writer.EndCell();

                    decimal value = 0;
                    decimal taxExempt = 0;
                    foreach (Transaction t in subtotal.Value)
                    {
                        var amount = t.Amount;
                        if (t.Investment != null && t.Investment.Security != null && t.Investment.Security.Taxable == YesNo.No)
                        {
                            taxExempt += amount;
                        }
                        else
                        {
                            value += amount;
                        }
                    }

                    if (tc.DefaultSign < 0)
                    {
                        value = value * -1;
                    }

                    writer.StartCell();
                    writer.WriteNumber(value.ToString("C"));
                    writer.EndCell();

                    writer.StartCell();
                    if (taxExempt > 0)
                    {
                        writer.WriteNumber(taxExempt.ToString("C"));
                    }
                    writer.EndCell();
                    writer.EndRow();
                    sum += value;
                }

                writer.StartRow();
                writer.StartCell();
                writer.EndCell();
                writer.StartCell();
                writer.WriteNumber(sum.ToString("C"), FontStyles.Normal, FontWeights.Bold, null);
                writer.EndCell();
                writer.EndRow();

            }

            writer.EndTable();
        }

        string Rounded(decimal value, int decimals)
        {
            decimal rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            // for some odd reason decimal.ToString() always adds 3 decimal places so you get "23.000" instead of "23".
            double d = (double)rounded;
            return d.ToString();
        }

        /// <summary>
        /// In order to not owe the IRS anything, we want to round up the numbers and not mess with the half pennies.
        /// Technically we could file a rounding adjustment, but for a few pennies it's not worth the effort.
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        private static decimal GiveUpTheFractionalPennies(decimal x)
        {
            return Math.Ceiling(x * 100) / 100;
        }


        public override void Export(string filename)
        {
            TxfExporter exporter = new TxfExporter(this.money);
            exporter.Export(filename, year, this.capitalGainsOnly, this.consolidateOnDateSold);
        }


        private Button CreateExportTxfButton()
        {
            Button button = CreateReportButton("Icons/TurboTax.png", "Export", "Export .txf file format for TuboTax");

            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(10);

            button.Click += new RoutedEventHandler((s, args) =>
            {
                OnExportTaxInfoAsTxf();
            });
            return button;
        }

        private void OnExportTaxInfoAsTxf()
        {
            int year = this.year;

            SaveFileDialog fd = new SaveFileDialog();
            fd.CheckPathExists = true;
            fd.AddExtension = true;
            fd.Filter = "TXF File (.txf)|*.txf";
            fd.FileName = "Tax" + year;

            if (fd.ShowDialog(App.Current.MainWindow) == true)
            {
                try
                {
                    string filename = fd.FileName;
                    Export(filename);
                }
                catch (Exception ex)
                {
                    MessageBoxEx.Show(ex.Message, "Error Exporting .txf", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

    }

}
