﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using ListViewEmbeddedControls;
using YouTube_Downloader.Classes;

namespace YouTube_Downloader.Operations
{
    public class ConvertOperation : ListViewItem, IOperation, IDisposable
    {
        /// <summary>
        /// Gets the input file path.
        /// </summary>
        public string Input { get; private set; }
        /// <summary>
        /// Gets the output file path.
        /// </summary>
        public string Output { get; private set; }
        /// <summary>
        /// Gets the operation status.
        /// </summary>
        public OperationStatus Status { get; private set; }

        /// <summary>
        /// Occurs when the operation is complete.
        /// </summary>
        public event OperationEventHandler OperationComplete;

        bool remove;

        public ConvertOperation(string text)
            : base(text)
        {
        }

        ~ConvertOperation()
        {
            // Finalizer calls Dispose(false)
            Dispose(false);
        }

        /// <summary>
        /// Returns whether the output can be opened.
        /// </summary>
        public bool CanOpen()
        {
            return this.Status == OperationStatus.Success;
        }

        /// <summary>
        /// Returns whether the operation can be paused.
        /// </summary>
        public bool CanPause()
        {
            /* Doesn't support pausing. */
            return false;
        }

        /// <summary>
        /// Returns whether the operation can be resumed.
        /// </summary>
        public bool CanResume()
        {
            /* Doesn't support resuming. */
            return false;
        }

        /// <summary>
        /// Returns whether the operation can be stopped.
        /// </summary>
        public bool CanStop()
        {
            /* Can stop if working. */
            return this.Status == OperationStatus.Working;
        }

        /// <summary>
        /// Starts the converting operation, and optional cropping.
        /// </summary>
        /// <param name="input">The file to convert.</param>
        /// <param name="output">The path to save the converted file.</param>
        /// <param name="start">The start position to crop in 00:00:00.000 format. Can be null to not crop.</param>
        /// <param name="end">The end position to crop in 00:00:00.000 format. Can be null to crop to end, or not at all if start is also null.</param>
        public void Convert(string input, string output, TimeSpan start, TimeSpan end)
        {
            this.Input = input;
            this.Output = output;
            this.converterStart = start;
            this.converterEnd = end;

            backgroundWorker = new BackgroundWorker();
            backgroundWorker.WorkerReportsProgress = true;
            backgroundWorker.WorkerSupportsCancellation = true;
            backgroundWorker.DoWork += backgroundWorker_DoWork;
            backgroundWorker.ProgressChanged += backgroundWorker_ProgressChanged;
            backgroundWorker.RunWorkerCompleted += backgroundWorker_RunWorkerCompleted;
            backgroundWorker.RunWorkerAsync();

            Program.RunningOperations.Add(this);

            this.Status = OperationStatus.Working;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Free managed resources
                if (backgroundWorker != null)
                {
                    backgroundWorker.Dispose();
                    backgroundWorker = null;
                }
                if (process != null)
                {
                    process.Dispose();
                    process = null;
                }
                OperationComplete = null;
            }
        }

        /// <summary>
        /// Opens the output file.
        /// </summary>
        public bool Open()
        {
            try
            {
                Process.Start(this.Output);
            }
            catch
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Opens the output directory.
        /// </summary>
        public bool OpenContainingFolder()
        {
            try
            {
                Process.Start(Path.GetDirectoryName(this.Output));
            }
            catch
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Not supported.
        /// </summary>
        public void Pause()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Not supported.
        /// </summary>
        public void Resume()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Stops the operation.
        /// </summary>
        /// <param name="remove">True to remove the operation from it's ListView.</param>
        /// <param name="cleanup">True to delete unfinished files.</param>
        public bool Stop(bool remove, bool cleanup)
        {
            bool success = true;

            this.remove = remove;

            if (this.Status == OperationStatus.Paused || this.Status == OperationStatus.Working)
            {
                try
                {
                    backgroundWorker.CancelAsync();

                    if (process != null && !process.HasExited)
                        process.StandardInput.WriteLine("\x71");

                    this.Status = OperationStatus.Canceled;
                    this.RefreshStatus();
                }
                catch (Exception ex)
                {
                    Program.SaveException(ex);
                    return false;
                }
            }

            if (cleanup && this.Status != OperationStatus.Success)
            {
                if (File.Exists(this.Output))
                    Helper.DeleteFiles(this.Output);
            }

            OnOperationComplete(new OperationEventArgs(this, this.Status));

            return success;
        }

        #region backgroundWorker

        private BackgroundWorker backgroundWorker;
        private TimeSpan converterStart = TimeSpan.MinValue;
        private TimeSpan converterEnd = TimeSpan.MinValue;
        private Process process;

        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                FFmpegHelper.Convert(backgroundWorker, this.Input, this.Output);

                if (!backgroundWorker.CancellationPending && converterStart != TimeSpan.MinValue)
                {
                    if (converterEnd == TimeSpan.MinValue)
                    {
                        FFmpegHelper.Crop(backgroundWorker, this.Output, this.Output, converterStart);
                    }
                    else
                    {
                        FFmpegHelper.Crop(backgroundWorker, this.Output, this.Output, converterStart, converterEnd);
                    }
                }

                this.converterStart = this.converterEnd = TimeSpan.MinValue;

                if (backgroundWorker.CancellationPending)
                {
                    e.Result = OperationStatus.Canceled;
                }
                else
                {
                    e.Result = OperationStatus.Success;
                }
            }
            catch (Exception ex)
            {
                Program.SaveException(ex);
                e.Result = OperationStatus.Failed;
            }
        }

        private void backgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            this.GetProgressBar().Value = e.ProgressPercentage;

            if (e.UserState is Process)
            {
                // FFmpegHelper will return the ffmpeg process so it can be used to cancel.
                process = (Process)e.UserState;
            }
        }

        private void backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            this.Status = (OperationStatus)e.Result;
            this.RefreshStatus();

            if (this.Status == OperationStatus.Success)
            {
                this.SubItems[4].Text = Helper.GetFileSize(this.Output);
            }

            Program.RunningOperations.Remove(this);

            OnOperationComplete(new OperationEventArgs(this, this.Status));

            if (this.remove && this.ListView != null)
            {
                this.Remove();
            }
        }

        #endregion

        /// <summary>
        /// Returns the operation's ProgressBar.
        /// </summary>
        private ProgressBar GetProgressBar()
        {
            return (ProgressBar)((ListViewEx)this.ListView).GetEmbeddedControl(1, this.Index);
        }

        private void RefreshStatus()
        {
            if (this.Status == OperationStatus.Canceled)
            {
                this.SubItems[2].Text = "Canceled";
            }
            else if (this.Status == OperationStatus.Failed)
            {
                this.SubItems[2].Text = "Failed";
            }
            else if (this.Status == OperationStatus.Success)
            {
                this.SubItems[2].Text = "Completed";
            }
            else
            {
                this.SubItems[2].Text = "???";
            }
        }

        private void OnOperationComplete(OperationEventArgs e)
        {
            RefreshStatus();

            if (Program.RunningOperations.Contains(this))
                Program.RunningOperations.Remove(this);

            if (this.remove && this.ListView != null)
                this.Remove();

            if (OperationComplete != null)
                OperationComplete(this, e);

            Console.WriteLine(this.GetType().Name + ": Operation complete, status: " + this.Status);
        }
    }
}
