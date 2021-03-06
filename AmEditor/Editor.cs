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
using System.Xml.Linq;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace AmEditor
{
    internal partial class Editor : Form
    {

        //Параметры командной строки
        private Dictionary<string, string> command_line_params = new Dictionary<string, string>();

        //Сохраненные настройки программы
        private SettingsStorage settings_storage;

        //Текущий файл конфигурации
        private string current_file_name = "";
        private DateTime current_file_open_datetime;

        //Шаги задачи
        private List<ActivityStep> activity_steps = new List<ActivityStep>();

        //Путь до папки с плагинами и правила включения плагинов
        private string plugins_path = "";
        private List<PlugIncludeRule> plugins_include_rules = new List<PlugIncludeRule>();

        //Плагины и документация по ним
        private List<PlugInfo> plugins = new List<PlugInfo>();
        private Dictionary<string, XDocument> plugins_doc = new Dictionary<string, XDocument>();

        //Конфигурация языкового пакета
        private Language config_language = new Language("ru");
        private Language language = new Language("ru");
        private delegate string language_delegate(string text);
        private language_delegate _;

        //В ручную ли изменяется состояние формы?
        private bool is_manual_change_state = false;

        //Состояние формы
        private enum FormState { Display, Edit }
        private FormState form_state;

        private List<Type> ComboBoxValueTypes = new List<Type>() { typeof(Int16), typeof(Int32), typeof(Int64), 
            typeof(UInt16), typeof(UInt32), typeof(UInt64), 
            typeof(Decimal), typeof(Single), typeof(Double), typeof(Boolean) };

        private void LoadPlugins()
        {
            plugins.Clear();
            plugins_doc.Clear();
            string[] files = Directory.GetFiles(plugins_path, "*.dll", SearchOption.TopDirectoryOnly);
            foreach (string file in files)
            {
                bool include = false;
                FileInfo fi = new FileInfo(file);
                foreach (PlugIncludeRule pir in plugins_include_rules)
                {
                    if ((pir.PlugNameMask == "*") || (pir.PlugNameMask == fi.Name))
                        include = pir.IncludeRule == "include";
                }
                if (!include)
                    continue;
                try
                {
                    plugins.Add(new PlugInfo(file));
                }
                catch (AMException e)
                {
                    throw new AMException(_(e.Message));
                }
            }
            string[] files_doc = Directory.GetFiles(plugins_path, "*.xml", SearchOption.TopDirectoryOnly);
            foreach (string file in files_doc)
            {
                try
                {
                    XDocument xdoc = XDocument.Load(file);
                    string assembly_name = xdoc.Root.Element("assembly").Element("name").Value;
                    plugins_doc.Add(assembly_name, xdoc);
                }
                catch { } //Если произошла ошибка загрузки документации, просто игнорировать
            }
        }

        private void LoadConfigFile(string fileName)
        {
            this.plugins_include_rules.Clear();
            this.activity_steps.Clear();
            XDocument xdoc = XDocument.Load(fileName, LoadOptions.PreserveWhitespace);
            if (xdoc.Root.Name.LocalName != "activity")
                throw new AMException("[config.xml]" + _("Корневой элемент файла конфигурации неизвестен"));
            //Обрабатываем элементы step
            IEnumerable<XElement> elements = xdoc.Root.Elements("step");
            foreach (XElement element in elements)
                 activity_steps.Add(ActivityStep.ConvertXElementToActivityStep(element, this.language));
            //Обрабатываем элемент plugins
            XElement xplugins = xdoc.Root.Element("plugins");
            if (xplugins != null)
            {
                IEnumerable<XElement> xplugins_include_rules = xplugins.Elements();
                foreach (XElement xplugin_include_rule in xplugins_include_rules)
                {
                    switch (xplugin_include_rule.Name.LocalName)
                    {
                        case "include":
                        case "exclude": this.plugins_include_rules.Add(new PlugIncludeRule(
                            xplugin_include_rule.Name.LocalName,
                            xplugin_include_rule.Value));
                            break;
                        default:
                            throw new AMException("[config.xml]" + _("Неизвестное правило для фильтрации плагинов"));
                    }
                }
            }
            //Обрабатываем элемент language
            XElement xlanguage = xdoc.Root.Element("language");
            if (xlanguage != null)
                this.config_language = new Language(xlanguage.Value);
        }

        public void PrepareConfig()
        {
            for (int i = 0; i < activity_steps.Count; i++)
            {
                ActivityStep step = activity_steps[i];
                CheckAndPrepareActions(ref step);
            }
        }

        public void CheckAndPrepareActions(ref ActivityStep step)
        {
            //Проверка и сопоставление plugin_name и action
            if (step.PlugName == null)
            {
                bool finded = false;
                foreach (PlugInfo plugin in plugins)
                {
                    if (plugin.HasAction(step.ActionName, PlugActionHelper.ConvertActivityStepToPlugParameters(step.InputParameters, step.OutputParameters)))
                    {
                        if (!finded)
                        {
                            step.PlugName = plugin.PlugName;
                            finded = true;
                        }
                        else
                            throw new AMException(String.Format(CultureInfo.CurrentCulture,
                                _("Неоднозначность определения заданного действия. Действие \"{0}\" определено в плагинах {1} и {2}. Необходимо явное указание плагина в файле конфигурации"),
                                step.ActionName, step.PlugName, plugin.PlugName));
                    }
                }
                if (!finded)
                    throw new AMException(String.Format(CultureInfo.CurrentCulture,_("Не удалось найти действие {0} ни в одном из плагинов"),
                        step.ActionName));
            }
            else
                foreach (PlugInfo plugin in plugins)
                {
                    if ((plugin.PlugName == step.PlugName) && 
                        (!plugin.HasAction(step.ActionName, PlugActionHelper.ConvertActivityStepToPlugParameters(step.InputParameters, step.OutputParameters))))
                        throw new AMException(String.Format(CultureInfo.CurrentCulture,_("В плагине {0} не определено действие {1}"),
                            plugin.PlugName, step.ActionName));
                }
        }

        //Загрузить список этапов выполнения в DataGridView
        private void LoadDataGridViewSteps()
        {
            dataGridViewSteps.Rows.Clear();
            int i = 0;
            foreach (ActivityStep step in activity_steps)
            {
                i++;
                dataGridViewSteps.Rows.Add(new object[] { i, step });
            }
        }

        //Загрузить список плагинов в выпадающий список
        private void LoadPluginsComboBox()
        {
            pluginName_comboBox.Items.Clear();
            foreach (PlugInfo plugin in plugins)
                pluginName_comboBox.Items.Add(plugin);
        }

        //Изменить состояние формы
        private void ChangeFormState()
        {
            buttonDel.Enabled = dataGridViewSteps.SelectedRows.Count > 0;
            buttonUp.Enabled = dataGridViewSteps.SelectedRows.Count > 0 && dataGridViewSteps.SelectedRows[0].Index > 0;
            buttonDown.Enabled = dataGridViewSteps.SelectedRows.Count > 0 && 
                dataGridViewSteps.SelectedRows[0].Index < (dataGridViewSteps.Rows.Count - 1);
            Text = _("AM-редактор");
            if (form_state == FormState.Edit)
                Text += " [*]";
            if (!String.IsNullOrEmpty(current_file_name))
                Text += String.Format(CultureInfo.CurrentCulture," [{0}]", current_file_name);
            pluginName_comboBox.Enabled = (dataGridViewSteps.SelectedRows.Count > 0);
            actionName_comboBox.Enabled = (dataGridViewSteps.SelectedRows.Count > 0);
        }

        //Разрешить перевод формы в состояние display
        private bool SetDisplayFormState()
        {
            if (form_state == FormState.Edit)
            {
                DialogResult dr = MessageBox.Show(_("Вы хотите сохранить изменения?"), _("Внимание"),
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (dr == DialogResult.No)
                {
                    form_state = FormState.Display;
                    return true;
                }
                if (dr == DialogResult.Yes)
                    return Save();
                return false;
            }
            return true;
        }

        //Сохранить файл конфигурации
        private bool SaveFile(string FileName)
        {
            XDocument document = new XDocument(new XDeclaration("1.0","UTF-8",""), new XElement("activity"));
            XElement activity_element = document.Root;
            if (plugins_include_rules.Count > 0)
            {
                XElement plugins_element = new XElement("plugins");
                foreach (PlugIncludeRule pir in plugins_include_rules)
                {
                    XElement include_element = new XElement(pir.IncludeRule);
                    include_element.Value = pir.PlugNameMask;
                    plugins_element.Add(include_element);
                }
                activity_element.Add(plugins_element);
            }
            activity_element.Add(new XElement("language", new XText(config_language.Prefix)));
            if (activity_steps.Count > 0)
            {
                int i = 1;
                foreach (ActivityStep step in activity_steps)
                {
                    if (step.PlugName == null || step.ActionName == null)
                    {
                        MessageBox.Show(String.Format(CultureInfo.CurrentCulture,_("Ошибка конфигурации шага выполнения №{0}. Данные не могут быть сохранены"), i), 
                            _("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                    i++;
                    XElement step_element = new XElement("step", new XAttribute("plugin", step.PlugName), 
                        new XAttribute("action", step.ActionName), new XAttribute("repeat", step.RepeatCount));
                    if (step.InputParameters.Count > 0)
                    {
                        XElement input_parameters = new XElement("input");
                        foreach (ActivityStepParameter asp in step.InputParameters)
                            input_parameters.Add(new XElement("parameter", new XAttribute("name", asp.Name), new XText(asp.Value)));
                        step_element.Add(input_parameters);
                    }
                    if (step.OutputParameters.Count > 0)
                    {
                        XElement output_parameters = new XElement("output");
                        foreach (ActivityStepParameter asp in step.OutputParameters)
                            output_parameters.Add(new XElement("parameter", new XAttribute("name", asp.Name), new XText(asp.Value)));
                        step_element.Add(output_parameters);
                    }
                    activity_element.Add(step_element);
                }
            }
            try
            {
                document.Save(FileName);
                return true;
            }
            catch(IOException e)
            {
                MessageBox.Show(e.Message, _("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool SaveSettings()
        {
            settings_storage.OpenFileHistory.Sort();
            //Если в списке истории больше 20 файлов, то оставить только 20 самых новых
            if (settings_storage.OpenFileHistory.Count > 20)
                settings_storage.OpenFileHistory.RemoveRange(0, settings_storage.OpenFileHistory.Count - 20);
            return SettingsStorage.SaveSettings(settings_storage);
        }

        //Сохранить
        private bool Save()
        {
            if (File.Exists(current_file_name))
            {
                if (SaveFile(current_file_name))
                {
                    form_state = FormState.Display;
                    ChangeFormState();
                    SaveOpenFileState();
                    return true;
                }
            }
            else
                return SaveAs();
            return false;
        }

        //Сохранить как...
        private bool SaveAs()
        {
            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                if (SaveFile(saveFileDialog1.FileName))
                {
                    current_file_name = saveFileDialog1.FileName;
                    current_file_open_datetime = DateTime.Now;
                    form_state = FormState.Display;
                    ChangeFormState();
                    SaveOpenFileState();
                    return true;
                }
            }
            return false;
        }

        private void SaveOpenFileState()
        {
            //Добавляем информацию о только что сохраненном файле в историю открытых файлов
            bool is_exists = false;
            foreach (OpenFileHistoryItem item in settings_storage.OpenFileHistory)
                if (item.FileName == current_file_name)
                {
                    //Если запись существует, то мы просто ее обновляем
                    item.CommandLineParams.Clear();
                    item.OpenDateTime = current_file_open_datetime;
                    foreach (string key in command_line_params.Keys)
                        item.CommandLineParams.Add(key, command_line_params[key]);
                    is_exists = true;
                }
            if (!is_exists)
            {
                //Если запись не существует, то добавляем новую
                OpenFileHistoryItem ofhi = new OpenFileHistoryItem();
                ofhi.FileName = current_file_name;
                ofhi.OpenDateTime = current_file_open_datetime;
                ofhi.CommandLineParams = command_line_params;
                settings_storage.OpenFileHistory.Add(ofhi);
            }
        }

        //Транслятор языка
        private void InterfaceLanguageReload()
        {
            конфигурацияToolStripMenuItem.Text = _("Конфигурация");
            настройкаToolStripMenuItem1.Text = _("Настройка");
            создатьToolStripMenuItem.Text = _("Создать");
            открытьToolStripMenuItem.Text = _("Открыть");
            сохранитьToolStripMenuItem.Text = _("Сохранить");
            сохранитьКакToolStripMenuItem1.Text = _("Сохранить как");
            копироватьСтрокуЗапускаToolStripMenuItem.Text = _("Копировать строку выполнения");
            выполнитьToolStripMenuItem.Text = _("Выполнить");
            выходToolStripMenuItem3.Text = _("Выход");
            плагиныToolStripMenuItem2.Text = _("Плагины");
            параметрыКоманднойСтрокиToolStripMenuItem.Text = _("Параметры командной строки");
            языкToolStripMenuItem2.Text = _("Язык");
            dataGridViewSteps.Columns["StepName"].HeaderText = _("Шаг");
            label1.Text = _("Действие");
            label2.Text = _("Плагин");
            label3.Text = _("Параметры");
            label4.Text = _("Описание");
            dataGridViewParams.Columns["ParamName"].HeaderText = _("Имя");
            dataGridViewParams.Columns["ParamType"].HeaderText = _("Тип");
            dataGridViewParams.Columns["ParamDirection"].HeaderText = _("Направление");
            dataGridViewParams.Columns["ParamValue"].HeaderText = _("Значение");
            ShowActionDescription();
            ChangeFormState();
        }

        public Editor()
        {
            InitializeComponent();

            //Загружаем настройки, если файл настроек существует либо инициализируем настройки по умолчанию
            settings_storage = SettingsStorage.LoadSettings();
            if (settings_storage != null)
            {
                language = new Language(settings_storage.InterfaceLanguagePrefix);
            }
            else
                settings_storage = new SettingsStorage();
            //инициируем переводчик по умолчанию
            _ = language.Translate;

            //Задаем путь до папки с плагинами по умолчанию
            plugins_path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            if (!Directory.Exists(plugins_path))
            {
                MessageBox.Show(String.Format(CultureInfo.CurrentCulture,_("Путь до папки {0} не найден"), plugins_path), _("Ошибка"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

            //Настраиваем состояине формы
            InterfaceLanguageReload();
            form_state = FormState.Display;
            this.FormClosing += new FormClosingEventHandler(Editor_FormClosing);
        }

        void Editor_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!SetDisplayFormState())
                e.Cancel = true;
            //Сохраняем состояние программы
            SaveSettings();
        }

        private void плагиныToolStripMenuItem2_Click(object sender, EventArgs e)
        {
            using (FormPlugins fp = new FormPlugins(plugins_include_rules, language))
                if (fp.ShowDialog() == DialogResult.OK)
                {
                    this.plugins_include_rules = fp.PluginsIncludeRules;
                    try
                    {
                        LoadPlugins();
                        LoadPluginsComboBox();
                        form_state = FormState.Edit;
                        ChangeFormState();
                    }
                    catch (Exception err)
                    {
                        MessageBox.Show(err.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
        }

        private void открытьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                //Если есть несохраненные данные, то сохраняем их
                if (!SetDisplayFormState())
                    return;
                //Обновляем состояние внутренних переменных
                current_file_name = openFileDialog1.FileName;
                current_file_open_datetime = DateTime.Now;
                command_line_params.Clear();
                try
                {
                    //Загружаем информацию
                    LoadConfigFile(current_file_name);
                    LoadPlugins();
                    PrepareConfig();
                    LoadPluginsComboBox();
                    LoadDataGridViewSteps();

                    //Обновляем состояние формы
                    ChangeFormState();

                    //Если в истории открытых файлов есть данный файл, то обновляем время открытия и загружаем список параметров командной строки
                    bool is_exists = false;
                    foreach (OpenFileHistoryItem item in settings_storage.OpenFileHistory)
                        if (item.FileName == current_file_name)
                        {
                            item.OpenDateTime = current_file_open_datetime;
                            foreach (string key in item.CommandLineParams.Keys)
                                command_line_params.Add(key, item.CommandLineParams[key]);
                            is_exists = true;
                        }
                    //Добавляем информацию об открытом файле в историю открытых файлов если его не существует
                    if (!is_exists)
                    {
                        OpenFileHistoryItem ofhi = new OpenFileHistoryItem();
                        ofhi.FileName = current_file_name;
                        ofhi.OpenDateTime = current_file_open_datetime;
                        ofhi.CommandLineParams = command_line_params;
                        settings_storage.OpenFileHistory.Add(ofhi);
                    }
                }
                catch (Exception err)
                {
                    MessageBox.Show(err.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void dataGridViewSteps_SelectionChanged(object sender, EventArgs e)
        {
            ChangeFormState();
            is_manual_change_state = false;
            pluginName_comboBox.SelectedIndex = -1;
            actionName_comboBox.SelectedIndex = -1;
            dataGridViewParams.Rows.Clear();
            richTextBoxDescription.Clear();
            if (dataGridViewSteps.SelectedRows.Count == 0)
                return;
            ActivityStep step = ((ActivityStep)dataGridViewSteps.SelectedRows[0].Cells["StepName"].Value);
            string plugin_name = step.PlugName;
            string action_name = step.ActionName;
            //Выбираем плагин из списка
            if (plugin_name == null)
            {
                is_manual_change_state = true;
                return;
            }
            for (int i = 0; i < pluginName_comboBox.Items.Count; i++)
            {
                if (((PlugInfo)pluginName_comboBox.Items[i]).PlugName == plugin_name)
                {
                    pluginName_comboBox.SelectedIndex = i;
                    break;
                }
            }
            if (pluginName_comboBox.SelectedIndex == -1)
            {
                MessageBox.Show(String.Format(CultureInfo.CurrentCulture,_("Неизвестный плагин {0}"), plugin_name), _("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                is_manual_change_state = true;
                return;
            }
            //Выбираем действие из списка
            if (action_name == null)
            {
                is_manual_change_state = true;
                return;
            }
            PlugInfo plugin = (PlugInfo)pluginName_comboBox.Items[pluginName_comboBox.SelectedIndex];
            for (int i = 0; i < actionName_comboBox.Items.Count; i++)
            {
                PlugActionInfo action = (PlugActionInfo)actionName_comboBox.Items[i];
                if (action.ActionName != step.ActionName)
                    continue;
                if (action.Parameters.Count != (step.InputParameters.Count + step.OutputParameters.Count))
                    continue;
                bool action_is_equal = true;
                foreach (PlugActionParameter parameter in action.Parameters)
                {
                    bool parameter_founded = false;
                    foreach (PlugActionParameter chk_parameter in 
                        PlugActionHelper.ConvertActivityStepToPlugParameters(step.InputParameters, step.OutputParameters))
                    {
                        if ((chk_parameter.Name == parameter.Name) && (chk_parameter.Direction == parameter.Direction))
                        {
                            parameter_founded = true;
                            break;
                        }
                    }
                    if (!parameter_founded)
                    {
                        action_is_equal = false;
                        break;
                    }
                }
                if (action_is_equal)
                {
                    actionName_comboBox.SelectedIndex = i;
                    break;
                }
            }
            is_manual_change_state = true;
            if (actionName_comboBox.SelectedIndex == -1)
            {
                MessageBox.Show(String.Format(CultureInfo.CurrentCulture,_("Неизвестное действие {0}"), action_name), _("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        private void создатьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!SetDisplayFormState())
                return;
            activity_steps.Clear();
            plugins_include_rules.Clear();
            plugins.Clear();
            plugins_doc.Clear();
            command_line_params.Clear();
            LoadDataGridViewSteps();
            LoadPluginsComboBox();
            current_file_name = "";
            current_file_open_datetime = DateTime.Now;
            ChangeFormState();
        }

        private void pluginName_comboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            actionName_comboBox.Items.Clear();
            if (pluginName_comboBox.SelectedIndex == -1)
                return;
            PlugInfo plugin = (PlugInfo)pluginName_comboBox.SelectedItem;
            foreach (PlugActionInfo pai in plugin.PlugActions)
                actionName_comboBox.Items.Add(pai);
            if (is_manual_change_state)
            {
                activity_steps[dataGridViewSteps.SelectedRows[0].Index].PlugName = plugin.PlugName;
                form_state = FormState.Edit;
                ChangeFormState();
            }
        }

        private void ShowActionDescription()
        {
            PlugActionInfo pai = (PlugActionInfo)actionName_comboBox.SelectedItem;
            PlugInfo plugin = (PlugInfo)pluginName_comboBox.SelectedItem;
            if (plugin == null || pai == null)
                return;
            string PluginName = plugin.PlugName;
            string ReleazeClassName = plugin.RealizeClassName();
            string ActionName = pai.ActionName;
            string ActionParametersList = "";
            foreach (PlugActionParameter pap in pai.Parameters)
                ActionParametersList += pap.ParameterType.FullName.Replace("&", "@") + ",";
            ActionParametersList = ActionParametersList.Trim(new char[] { ',' });
            if (plugins_doc.ContainsKey(PluginName))
            {
                XDocument plugin_doc = plugins_doc[PluginName];
                //Формат атрибута name элемента member в файле документации
                string element_name = "M:" + PluginName + "." + ReleazeClassName + "." + ActionName + "(" + ActionParametersList + ")";
                foreach (XElement element in plugin_doc.Root.Element("members").Elements())
                {
                    if (element.Attribute("name").Value == element_name)
                    {
                        //Это искомое описание метода, отформатировать и вывести в поле описания
                        string summary = element.Element("summary").Value.Trim();
                        Dictionary<string, string> params_names = new Dictionary<string, string>();
                        foreach (XElement param_element in element.Elements("param"))
                            params_names.Add(param_element.Attribute("name").Value, param_element.Value.Trim());
                        string Description = _("Описание") + ": " + summary;
                        foreach (string key in params_names.Keys)
                        {
                            string param_value = params_names[key];
                            Description += Environment.NewLine;
                            Description += _("Параметр") + " [" + key + "]: " + param_value;
                        }
                        richTextBoxDescription.Text = Description;
                        richTextBoxDescription.Select(0, (_("Описание") + ": ").Length);
                        richTextBoxDescription.SelectionFont = new Font(richTextBoxDescription.Font, FontStyle.Bold);
                        foreach (string key in params_names.Keys)
                        {
                            richTextBoxDescription.Select(
                                richTextBoxDescription.Text.IndexOf(_("Параметр") + " [" + key + "]: ", StringComparison.CurrentCulture),
                                (_("Параметр") + " [" + key + "]: ").Length);
                            richTextBoxDescription.SelectionFont = new Font(richTextBoxDescription.Font, FontStyle.Bold);
                            richTextBoxDescription.Select(
                                richTextBoxDescription.Text.IndexOf("[" + key + "]", StringComparison.CurrentCulture), ("[" + key + "]").Length);
                            richTextBoxDescription.SelectionColor = Color.Blue;
                        }
                    }
                }
            }
        }

        private void actionName_comboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            //Загружаем параметры действия и их значения (если они заданы)
            dataGridViewParams.Rows.Clear();
            richTextBoxDescription.Clear();
            if (actionName_comboBox.SelectedIndex == -1)
                return;
            PlugActionInfo pai = (PlugActionInfo)actionName_comboBox.SelectedItem;
            int i = 0;
            if (is_manual_change_state && dataGridViewSteps.SelectedRows.Count > 0)
            {
                ActivityStep step = ((ActivityStep)dataGridViewSteps.SelectedRows[0].Cells["StepName"].Value);
                step.InputParameters.Clear();
                step.OutputParameters.Clear();
                foreach (PlugActionParameter pap in pai.Parameters)
                {
                    if (pap.Direction == AMClasses.ParameterDirection.Input)
                        step.InputParameters.Add(new ActivityStepParameter(pap.Name, ""));
                    else
                        step.OutputParameters.Add(new ActivityStepParameter(pap.Name, ""));
                }
            }
            foreach (PlugActionParameter pap in pai.Parameters)
            {
                i++;
                if (dataGridViewSteps.SelectedRows.Count == 0)
                {
                    dataGridViewParams.Rows.Add(new object[] { i, pap.Name, pap.ParameterType, pap.Direction, null });
                    continue;
                }
                //Если выбран шаг, то подгрузить значения для него
                object value = null;
                ActivityStep step = ((ActivityStep)dataGridViewSteps.SelectedRows[0].Cells["StepName"].Value);
                foreach (ActivityStepParameter asp in step.InputParameters)
                {
                    if (pap.Name == asp.Name)
                    {
                        value = asp.Value;
                        break;
                    }
                }
                foreach (ActivityStepParameter asp in step.OutputParameters)
                {
                    if (pap.Name == asp.Name)
                    {
                        value = asp.Value;
                        break;
                    }
                }
                dataGridViewParams.Rows.Add(new object[] { i, pap.Name, pap.ParameterType, pap.Direction, value});
            }
            //Если есть документация в plugins_doc по данному действию, то получить ее
            ShowActionDescription();
            //Обновить состояние формы
            if (is_manual_change_state)
            {
                activity_steps[dataGridViewSteps.SelectedRows[0].Index].ActionName = pai.ActionName;
                form_state = FormState.Edit;
                ChangeFormState();
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ActivityStep step = new ActivityStep();
            int rowIndex = 0;
            if (dataGridViewSteps.CurrentCell != null)
                rowIndex = dataGridViewSteps.CurrentCell.RowIndex + 1;
            activity_steps.Insert(rowIndex, step);
            LoadDataGridViewSteps();
            dataGridViewSteps.Rows[rowIndex].Selected = true;
            dataGridViewSteps.CurrentCell = dataGridViewSteps.Rows[rowIndex].Cells[0];
            form_state = FormState.Edit;
            ChangeFormState();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (dataGridViewSteps.SelectedRows.Count == 0)
                return;
            ActivityStep step = ((ActivityStep)dataGridViewSteps.SelectedRows[0].Cells["StepName"].Value);
            int index = dataGridViewSteps.SelectedRows[0].Index;
            activity_steps.Remove(step);
            LoadDataGridViewSteps();
            if (dataGridViewSteps.RowCount > 0)
            {
                dataGridViewSteps.Rows[index > (dataGridViewSteps.Rows.Count - 1) ? (dataGridViewSteps.Rows.Count - 1) : index].Selected = true;
                dataGridViewSteps.CurrentCell = dataGridViewSteps.
                    Rows[index > (dataGridViewSteps.Rows.Count - 1) ? (dataGridViewSteps.Rows.Count - 1) : index].Cells[0];
            }
            form_state = FormState.Edit;
            ChangeFormState();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            int index = dataGridViewSteps.SelectedRows[0].Index;
            ActivityStep step = activity_steps[index];
            activity_steps[index] = activity_steps[index - 1];
            activity_steps[index - 1] = step;
            LoadDataGridViewSteps();
            dataGridViewSteps.Rows[index - 1].Selected = true;
            dataGridViewSteps.CurrentCell = dataGridViewSteps.Rows[index - 1].Cells[0];
            form_state = FormState.Edit;
            ChangeFormState();
        }

        private void button5_Click(object sender, EventArgs e)
        {
            int index = dataGridViewSteps.SelectedRows[0].Index;
            ActivityStep step = activity_steps[index];
            activity_steps[index] = activity_steps[index + 1];
            activity_steps[index + 1] = step;
            LoadDataGridViewSteps();
            dataGridViewSteps.Rows[index + 1].Selected = true;
            dataGridViewSteps.CurrentCell = dataGridViewSteps.Rows[index +1].Cells[0];
            form_state = FormState.Edit;
            ChangeFormState();
        }

        int rowIndexFromMouseDown;
        DataGridViewRow rw;

        private void dataGridViewSteps_MouseClick(object sender, MouseEventArgs e)
        {
            if (dataGridViewSteps.SelectedRows.Count == 1)
            {
                if (e.Button == MouseButtons.Left)
                {
                    rw = dataGridViewSteps.SelectedRows[0];
                    rowIndexFromMouseDown = dataGridViewSteps.SelectedRows[0].Index;
                    dataGridViewSteps.DoDragDrop(rw, DragDropEffects.Move);
                }
            }
        }

        private void dataGridViewSteps_DragDrop(object sender, DragEventArgs e)
        {
            int rowIndexOfItemUnderMouseToDrop;
            Point clientPoint = dataGridViewSteps.PointToClient(new Point(e.X, e.Y));
            rowIndexOfItemUnderMouseToDrop = dataGridViewSteps.HitTest(clientPoint.X, clientPoint.Y).RowIndex;
            if ((e.Effect == DragDropEffects.Move) && (rowIndexOfItemUnderMouseToDrop != -1) && (rowIndexOfItemUnderMouseToDrop != rowIndexFromMouseDown))
            {
                ActivityStep step = activity_steps[rowIndexFromMouseDown];
                activity_steps.Remove(step);
                activity_steps.Insert(rowIndexOfItemUnderMouseToDrop, step);
                LoadDataGridViewSteps();
                dataGridViewSteps.Rows[rowIndexOfItemUnderMouseToDrop].Selected = true;
                dataGridViewSteps.CurrentCell = dataGridViewSteps.Rows[rowIndexOfItemUnderMouseToDrop].Cells[0];
                form_state = FormState.Edit;
                ChangeFormState();
            }
        }

        private void dataGridViewSteps_DragEnter(object sender, DragEventArgs e)
        {
            if (dataGridViewSteps.SelectedRows.Count > 0)
                e.Effect = DragDropEffects.Move;
        }

        //Поиск списка видимых глобальных параметров указанного типа на данном шаге выполнения
        private List<string> GlobalParametersNamesBy(Type type, int step_index)
        {
            List<string> global_parameters = new List<string>();
            for (int i = 0; i < step_index; i++)
            {
                PlugInfo current_plugin = null;
                foreach (PlugInfo plugin in plugins)
                    if (plugin.PlugName == activity_steps[i].PlugName)
                        current_plugin = plugin;
                PlugActionInfo current_action = null;
                foreach (PlugActionInfo pai in current_plugin.PlugActions)
                    if (pai.ActionName == activity_steps[i].ActionName)
                        current_action = pai;
                foreach (PlugActionParameter param in current_action.Parameters)
                    if ((param.Direction == AMClasses.ParameterDirection.Output) && (param.ParameterType.FullName.Replace("&", "") == type.FullName))
                    {
                        foreach (ActivityStepParameter asp in activity_steps[i].OutputParameters)
                            if (asp.Name == param.Name)
                            {
                                if (!String.IsNullOrEmpty(asp.Value.Trim()))
                                {
                                    if (!global_parameters.Contains("[" + asp.Value + "]"))
                                        global_parameters.Add("["+asp.Value+"]");
                                }
                                else
                                {
                                    if (!global_parameters.Contains("[" + asp.Value + "]"))
                                        global_parameters.Add("["+asp.Name+"]");
                                }
                            }
                    }
            }
            return global_parameters;
        }

        private void SetCurrentActiveStepParameterValue(string value)
        {
            string param_name = (string)dataGridViewParams.SelectedRows[0].Cells["ParamName"].Value;
            for (int i = 0; i < activity_steps[dataGridViewSteps.SelectedRows[0].Index].InputParameters.Count; i++)
            {
                if (activity_steps[dataGridViewSteps.SelectedRows[0].Index].InputParameters[i].Name == param_name)
                    activity_steps[dataGridViewSteps.SelectedRows[0].Index].InputParameters[i].Value = value;
            }
            for (int i = 0; i < activity_steps[dataGridViewSteps.SelectedRows[0].Index].OutputParameters.Count; i++)
            {
                if (activity_steps[dataGridViewSteps.SelectedRows[0].Index].OutputParameters[i].Name == param_name)
                    activity_steps[dataGridViewSteps.SelectedRows[0].Index].OutputParameters[i].Value = value;
            }
            dataGridViewParams.SelectedRows[0].Cells["ParamValue"].Value = value;
        }

        private string GetCurrentActiveStepParameterValue()
        {
            return dataGridViewParams.SelectedRows[0].Cells["ParamValue"].Value.ToString();
        }

        private void dataGridViewParams_DoubleClick(object sender, EventArgs e)
        {
            if (dataGridViewParams.SelectedRows.Count == 0)
                return;
            Type param_type = (Type)dataGridViewParams.SelectedRows[0].Cells["ParamType"].Value;
            string param_name = (string)dataGridViewParams.SelectedRows[0].Cells["ParamName"].Value;
            AMClasses.ParameterDirection direction = 
                (AMClasses.ParameterDirection)dataGridViewParams.SelectedRows[0].Cells["ParamDirection"].Value;
            if (direction == AMClasses.ParameterDirection.Output)
            {
                using (FormStringValue fsv = new FormStringValue(language))
                {
                    fsv.Text = _("Задать глобальное имя") + " [" + param_name + "]";
                    fsv.Value = GetCurrentActiveStepParameterValue();
                    if (fsv.ShowDialog() == DialogResult.OK)
                    {
                        SetCurrentActiveStepParameterValue(fsv.Value);
                        form_state = FormState.Edit;
                        ChangeFormState();
                    }
                }
                return;
            }
            List<string> values = GlobalParametersNamesBy(param_type, dataGridViewSteps.SelectedRows[0].Index);
            foreach (string key in command_line_params.Keys)
                if (!values.Contains("[" + key + "]"))
                    values.Add("[" + key + "]");
            //Если тип данных перечисление
            if (param_type.IsEnum)
            {
                string[] names = Enum.GetNames(param_type);
                foreach (string name in names)
                    values.Add(name);
                using (FormComboBoxValue fcbv = new FormComboBoxValue(values, language))
                {
                    fcbv.Text = _("Задать значение параметра") + " [" + param_name + "]";
                    fcbv.value = GetCurrentActiveStepParameterValue();
                    if (fcbv.ShowDialog() == DialogResult.OK)
                    {
                        SetCurrentActiveStepParameterValue(fcbv.value);
                        form_state = FormState.Edit;
                        ChangeFormState();
                    }
                }
                return;
            }
            //Если тип является одним из стандартных типов данных вроде числа, даты, логического типа
            bool is_combobox_type = false;
            foreach (Type type in ComboBoxValueTypes)
                if (param_type == type)
                    is_combobox_type = true;
            if (is_combobox_type)
            {
                if (param_type == typeof(Boolean))
                {
                    values.Add("true");
                    values.Add("false");
                }
                using (FormComboBoxValue fcbv = new FormComboBoxValue(values, language))
                {
                    fcbv.Text = _("Задать значение параметра") + " [" + param_name + "]";
                    fcbv.value = GetCurrentActiveStepParameterValue();
                    if (fcbv.ShowDialog() == DialogResult.OK)
                    {
                        SetCurrentActiveStepParameterValue(fcbv.value);
                        form_state = FormState.Edit;
                        ChangeFormState();
                    }
                }
                return;
            }
            //Если тип является строкой или не известен
            foreach (Type type in ComboBoxValueTypes)
                values.AddRange(GlobalParametersNamesBy(type, dataGridViewSteps.SelectedRows[0].Index));
            using (FormMultiValue fsqlv = new FormMultiValue(values, language))
            {
                fsqlv.Text = _("Задать значение параметра") + " [" + param_name + "]";
                fsqlv.Value = GetCurrentActiveStepParameterValue();
                if (fsqlv.ShowDialog() == DialogResult.OK)
                {
                    SetCurrentActiveStepParameterValue(fsqlv.Value);
                    form_state = FormState.Edit;
                    ChangeFormState();
                }
            }
        }

        private void параметрыКоманднойСтрокиToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (FormCommandLineParams fclp = new FormCommandLineParams(language))
            {
                fclp.command_line_params = command_line_params;
                if (fclp.ShowDialog() == DialogResult.OK)
                {
                    command_line_params = fclp.command_line_params;
                    form_state = FormState.Edit;
                    ChangeFormState();
                }
            }
        }

        private void сохранитьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Save();
        }

        private void сохранитьКакToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            SaveAs();
        }

        private void выполнитьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Save())
                return;
            string activityManager = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ActivityManager.exe");
            if (!File.Exists(activityManager))
            {
                MessageBox.Show(_("Не удалось найти исполняемый файл ActivityManager.exe"),
                    _("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string arguments = "config=\""+current_file_name+"\"";
            foreach(string key in command_line_params.Keys)
                arguments += " "+key+"=\""+command_line_params[key]+"\"";
            using (Process process = new Process())
            {
                ProcessStartInfo psi = new ProcessStartInfo(activityManager, arguments);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                process.StartInfo = psi;
                process.Start();
            }
        }

        private void выходToolStripMenuItem3_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void языкToolStripMenuItem2_Click(object sender, EventArgs e)
        {
            using (FormLanguage fl = new FormLanguage(language))
            {
                string lang_path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang");
                string[] files = Directory.GetFiles(lang_path);
                List<string> languages = new List<string>();
                languages.Add("ru");    //Язык по умолчанию
                foreach (string file in files)
                {
                    FileInfo fi = new FileInfo(file);
                    if (!languages.Contains(fi.Extension.Trim(new char[] { '.' })))
                        languages.Add(fi.Extension.Trim(new char[] { '.' }));
                }
                fl.languages = languages;
                fl.config_language = config_language.Prefix;
                fl.interface_language = language.Prefix;
                if (fl.ShowDialog() == DialogResult.OK)
                {
                    language = new Language(fl.interface_language);
                    settings_storage.InterfaceLanguagePrefix = fl.interface_language;
                    _ = language.Translate;
                    if (config_language.Prefix != fl.config_language)
                    {
                        config_language = new Language(fl.config_language);
                        form_state = FormState.Edit;
                        ChangeFormState();
                    }
                    InterfaceLanguageReload();
                }
            }
        }

        private void копироватьСтрокуЗапускаToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string activityManager = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ActivityManager.exe");
            string arguments = "config=\"" + current_file_name + "\"";
            foreach (string key in command_line_params.Keys)
                arguments += " " + key + "=\"" + command_line_params[key] + "\"";
            Clipboard.SetText(activityManager+" "+arguments);
            MessageBox.Show(_("Строка выполнения успешно скопирована"), _("Информация"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void dataGridViewParams_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                dataGridViewParams_DoubleClick(sender, e);
                e.Handled = true;
            }
        }
    }
}
