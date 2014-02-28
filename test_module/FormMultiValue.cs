﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using AMClasses;
using System.IO;

namespace AmEditor
{
    internal partial class FormMultiValue : Form
    {
        public string Value
        {
            get { return scintillaEditor.Text; }
            set { scintillaEditor.Text = value; }
        }

        public FormMultiValue(List<string> globalVariables, Language language)
        {
            InitializeComponent();
            foreach (string GlobalVariable in globalVariables)
                comboBoxValues.Items.Add(GlobalVariable);
            button1.Text = language.Translate(button1.Text);
            button2.Text = language.Translate(button2.Text);
            button4.Text = language.Translate(button4.Text);
            setEditorLanguage(mSSQLToolStripMenuItem, "mssql");
        }

        private void button4_Click(object sender, EventArgs e)
        {
            scintillaEditor.Selection.Text = comboBoxValues.Text;
        }

        private void отменитьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            scintillaEditor.UndoRedo.Undo();
        }

        private void повторитьToolStripMenuItem_Click(object sender, EventArgs e)
        {

            scintillaEditor.UndoRedo.Redo();
        }

        private void копироватьToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (scintillaEditor.Selection.Text != "")
                Clipboard.SetText(scintillaEditor.Selection.Text);
        }

        private void вырезатьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (scintillaEditor.Selection.Text != "")
                Clipboard.SetText(scintillaEditor.Selection.Text);
            scintillaEditor.Selection.Text = "";
        }

        private void удалитьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            scintillaEditor.Selection.Text = "";
        }

        private void буферОбменаToolStripMenuItem_Click(object sender, EventArgs e)
        {
            scintillaEditor.Selection.Text = Clipboard.GetText();
        }

        private void путьДоФайлаToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (openFileDialogInsert.ShowDialog() == DialogResult.OK)
                scintillaEditor.Selection.Text = openFileDialogInsert.FileName;
        }

        private void путьДоПапкиToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialogInsert.ShowDialog() == DialogResult.OK)
                scintillaEditor.Selection.Text = folderBrowserDialogInsert.SelectedPath;
        }

        private void setEditorLanguage(object sender, string language)
        {
            ToolStripMenuItem parent = (ToolStripMenuItem)((ToolStripMenuItem)sender).OwnerItem;
            foreach (var menu in parent.DropDown.Items)
                if (menu is ToolStripMenuItem)
                    ((ToolStripMenuItem)menu).Checked = false;
            scintillaEditor.AutoComplete.ListString = "";
            scintillaEditor.ConfigurationManager.Language = language;
            if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, language+".synax")))
            {
                using (StreamReader sr = new StreamReader(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, language + ".synax")))
                {
                    string autoComplete = sr.ReadToEnd();
                    autoComplete = autoComplete.Replace(Environment.NewLine, " ");
                    scintillaEditor.AutoComplete.ListString = autoComplete;
                }
            }
            ((ToolStripMenuItem)sender).Checked = true;
        }

        private void mSSQLToolStripMenuItem_Click(object sender, EventArgs e)
        {          
            setEditorLanguage(sender, "mssql");
        }

        private void javaScriptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setEditorLanguage(sender, "js");
        }

        private void безПодсветкиToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem parent = (ToolStripMenuItem)((ToolStripMenuItem)sender).OwnerItem;
            foreach (var menu in parent.DropDown.Items)
                if (menu is ToolStripMenuItem)
                    ((ToolStripMenuItem)menu).Checked = false;
            scintillaEditor.AutoComplete.ListString = "";
            scintillaEditor.ConfigurationManager.Language = "";
            ((ToolStripMenuItem)sender).Checked = true;
        }
    }
}
