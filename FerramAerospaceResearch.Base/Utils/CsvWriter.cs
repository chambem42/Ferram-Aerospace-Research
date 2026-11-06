using System;
using System.IO;

namespace FerramAerospaceResearch
{
    public class CsvWriter : IDisposable
    {
        private const string Separator = ", ";

        public CsvWriter(string filename)
        {
            Filename = filename;
        }

        public string Filename { get; private set; }

        protected StreamWriter Writer { get; private set; }
        public bool Appending { get; private set; }

        public bool IsOpen
        {
            get { return Writer != null; }
        }

        public void Dispose()
        {
            Writer?.Dispose();
        }

        public void Open()
        {
            Open(Filename);
        }

        public void Open(string filename)
        {
            Filename = filename;
            Appending = File.Exists(filename);
            Writer = File.AppendText(filename);
            Writer.AutoFlush = false;

            // don't write sep= to an existing file
            if (Appending)
                return;
            // excel needs "sep=..." as the first line to recognize this as csv
            Writer.Write("sep=");
            Writer.WriteLine(Separator);
        }

        public void Close()
        {
            Writer?.Close();
            Writer?.Dispose();
            Writer = null;
        }

        public void Write<T>(T[] values)
        {
            foreach (T value in values)
                Write(value);
        }

        public void Write<T>(T value)
        {
            Writer.Write(value.ToString());
            Writer.Write(Separator);
        }

        public void WriteLine<T>(T[] values)
        {
            Write(values);
            Writer.WriteLine();
        }

        public void WriteLine<T>(T value)
        {
            Write(value);
            Writer.WriteLine();
        }

        public void WriteLine()
        {
            Writer.WriteLine();
        }

        public void Flush()
        {
            Writer.Flush();
        }
    }
}
