﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Noesis.Javascript;
using System.IO;

//Модуль запуска скриптов
namespace JSModule
{
    /// <summary>
    /// Доступный из-вне интерфейс плагина
    /// </summary>
    public interface IPlug
    {
        /// <summary>
        /// Выполнить JavaScript
        /// </summary>
        /// <param name="script">Скрипт на языке JavaScript</param>
        /// <param name="result">Возвращаемое значение</param>
        void JSRun(string script, out object result);
    }

    /// <summary>
    /// Класс, реализующий интерфейс IPlug
    /// </summary>
    public class JSPlug: IPlug
    {
        /// <summary>
        /// Выполнить JavaScript
        /// </summary>
        /// <param name="script">Скрипт на языке JavaScript</param>
        /// <param name="result">Возвращаемое значение</param>
        public void JSRun(string script, out object result)
        {
            using (JavascriptContext context = new JavascriptContext())
            {
                context.SetParameter("result", null);
                string v = "function main() { var result = null; "+script+"; return result; }; var result = main();";
                context.Run(v);
                result = context.GetParameter("result");
            }
        }
    }
}
