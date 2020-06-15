// <copyright file="RunOnMainThread.cs" company="Google Inc.">
// Copyright (C) 2018 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Google {

/// <summary>
/// Runs tasks on the main thread in the editor.
/// If the editor is running in batch mode tasks will be executed synchronously on the current
/// thread.
/// </summary>
[InitializeOnLoad]
public class RunOnMainThread {

    /// <summary>
    /// Job that is executed in the future.
    /// </summary>
    private class ScheduledJob {

        /// <summary>
        /// Jobs scheduled for execution indexed by unique ID.
        /// </summary>
        private static Dictionary<int, ScheduledJob> scheduledJobs =
            new Dictionary<int, ScheduledJob>();

        /// <summary>
        /// Next ID for a job.
        /// </summary>
        private static int nextJobId = 1;

        /// <summary>
        /// Action to execute.
        /// </summary>
        private Action Job;

        /// <summary>
        /// ID of this job.
        /// </summary>
        private int JobId;

        /// <summary>
        /// Execution delay in milliseconds.
        /// </summary>
        private double DelayInMilliseconds;

        /// <summary>
        /// Time this job was scheduled
        /// </summary>
        private DateTime scheduledTime = DateTime.Now;

        /// <summary>
        /// Schedule a job which is executed after the specified delay.
        /// </summary>
        /// <param name="job">Action to execute.</param>
        /// <param name="delayInMilliseconds">Time to wait for execution of this job.</param>
        /// <returns>ID of the scheduled job (always non-zero).</returns>
        public static int Schedule(Action job, double delayInMilliseconds) {
            ScheduledJob scheduledJob;
            lock (scheduledJobs) {
                scheduledJob = new ScheduledJob {
                    Job = job,
                    JobId = nextJobId,
                    DelayInMilliseconds = ExecutionEnvironment.ExecuteMethodEnabled ? 0.0 :
                        delayInMilliseconds
                };
                scheduledJobs[nextJobId++] = scheduledJob;
                if (nextJobId == 0) nextJobId++;
            }
            RunOnMainThread.PollOnUpdateUntilComplete(scheduledJob.PollUntilExecutionTime);
            return scheduledJob.JobId;
        }

        /// <summary>
        /// Cancel a scheduled job.
        /// </summary>
        /// <param name="jobId">ID of previously scheduled job to cancel.</param>
        public static void Cancel(int jobId) {
            lock (scheduledJobs) {
                ScheduledJob scheduledJob;
                if (scheduledJobs.TryGetValue(jobId, out scheduledJob)) {
                    scheduledJob.Dequeue();
                }
            }
        }

        /// <summary>
        /// Remove this job from the set of scheduled jobs.
        /// </summary>
        /// <returns>Action associated with this job.</returns>
        private Action Dequeue() {
            Action job;
            lock (scheduledJobs) {
                scheduledJobs.Remove(JobId);
                JobId = 0;
                job = Job;
                Job = null;
            }
            return job;
        }

        /// <summary>
        /// Try to execute the scheduled job.
        /// </summary>
        /// <returns>true if the job can be executed, false otherwise.</returns>
        public bool PollUntilExecutionTime() {
            if (DateTime.Now.Subtract(scheduledTime).TotalMilliseconds < DelayInMilliseconds) {
                return false;
            }
            var job = Dequeue();
            if (job != null) job();
            return true;
        }
    }

    /// <summary>
    /// Enqueues jobs on the main thread to that need to be executed in serial order.
    /// </summary>
    public class JobQueue {

        /// <summary>
        /// Queue of jobs to execute.
        /// </summary>
        private Queue<Action> jobs = new Queue<Action>();

        /// <summary>
        /// Execute the next job.
        /// </summary>
        private void ExecuteNext() {
            var job = jobs.Peek();
            try {
                job();
            } catch (Exception e) {
                UnityEngine.Debug.LogError(
                    String.Format("Serial job {0} failed due to exception: {1}",
                                  job, e.ToString()));
                Complete();
            }
        }

        /// <summary>
        /// Schedule the execution of a job.
        /// </summary>
        /// <param name="job">Action that will be called in future. This job should call
        /// Complete() to signal the end of the operation.</param>
        public void Schedule(Action job) {
            RunOnMainThread.Run(() => {
                    jobs.Enqueue(job);
                    if (jobs.Count == 1) ExecuteNext();
                }, runNow: false);
        }

        /// <summary>
        /// Signal the end of job execution.
        /// </summary>
        public void Complete() {
            RunOnMainThread.Run(() => {
                    var remaining = jobs.Count;
                    if (remaining > 0) jobs.Dequeue();
                    if (remaining > 1) ExecuteNext();
                }, runNow: false);
        }
    }

    /// <summary>
    /// Job which calls a function periodically over an interval.
    /// </summary>
    public class PeriodicJob {

        /// <summary>
        /// ID of the next update.
        /// </summary>
        private int jobId;

        /// <summary>
        /// Closure to execute on each update.
        /// </summary>
        private Func<bool> condition;

        /// <summary>
        /// Interval to wait between each execution of the job.
        /// </summary>
        public double IntervalInMilliseconds;

        /// <summary>
        /// Construct a periodic job.
        /// </summary>
        /// <param name="condition">Method that returns true when the operation is complete, false
        /// otherwise.</param>
        public PeriodicJob(Func<bool> condition) {
            this.condition = condition;
        }

        /// <summary>
        /// Execute the condition and if it isn't complete, schedule the next execution.
        /// </summary>
        public void Execute() {
            if (condition != null) {
                if (!condition()) {
                    jobId = RunOnMainThread.Schedule(() => { Execute(); }, IntervalInMilliseconds);
                } else {
                    Stop();
                }
            }
        }

        /// <summary>
        /// Stop periodic execution of the job.
        /// </summary>
        public void Stop() {
            RunOnMainThread.Cancel(jobId);
            jobId = 0;
            condition = null;
        }
    }

    /// <summary>
    /// ID of the main thread.
    /// </summary>
    private static int mainThreadId;

    /// <summary>
    /// Queue of jobs to execute.
    /// </summary>
    private static Queue<Action> jobs = new Queue<Action>();

    /// <summary>
    /// Set of jobs to poll until complete.
    /// </summary>
    private static List<Func<bool>> pollingJobs = new List<Func<bool>>();

    /// <summary>
    /// Set of polling jobs that were complete after the last update.
    /// This is statically allocated to prevent an allocation each frame.
    /// </summary>
    private static List<Func<bool>> completePollingJobs = new List<Func<bool>>();

    /// <summary>
    /// Determine whether the current thread is the main thread.
    /// </summary>
    private static bool OnMainThread {
        get { return mainThreadId == System.Threading.Thread.CurrentThread.ManagedThreadId; }
    }

    /// <summary>
    /// Number of times ExecuteAll() has been called on the current thread.
    /// </summary>
    private static int runningExecuteAllCount = 0;

    /// <summary>
    /// Flag which indicates whether any jobs are running on the main thread.
    /// This is set and cleared by RunAction().
    /// </summary>
    private static bool runningJobs = false;

    /// <summary>
    /// Whether to execute a scheduled jobs immediately if the scheduling thread is the main thread.
    /// This property is reset to its' default value after each set of jobs is dispatched.
    /// </summary>
    public static bool ExecuteNow {
        get {
            return ExecutionEnvironment.ExecuteMethodEnabled && !runningJobs &&
                runningExecuteAllCount == 0;
        }
    }

    /// <summary>
    /// Initialize the ID of the main thread.  This class *must* be called on the main thread
    /// before use.
    /// </summary>
    static RunOnMainThread() {
        mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        // NOTE: This hooks ExecuteAll on the main thread here and never unregisters as we can't
        // register event handlers on any thread except for the main thread.
        OnUpdate += ExecuteAll;
    }

    /// <summary>
    /// Event which is called periodically when Unity isn't in batch mode.
    /// In batch mode, addition of an action results in it being called immediately and event
    /// removal does nothing.
    /// NOTE: This should not be called with a closure as it will not be possible to unregister
    /// the closure from the event.
    /// </summary>
    public static event EditorApplication.CallbackFunction OnUpdate {
        add { AddOnUpdateCallback(value); }
        remove { RemoveOnUpdateCallback(value); }
    }

    /// <summary>
    /// Add a callback from the EditorApplication.update event.  This is in a method to work
    /// around the mono compiler throwing an exception when this is included inline in the OnUpdate
    /// event.
    /// </summary>
    /// <param name="callback">Callback to add.</param>
    private static void AddOnUpdateCallback(EditorApplication.CallbackFunction callback) {
        // Try removing the existing event as Unity can end up calling the event multiple
        // times if a DLL is reloaded in the app domain.
        Run(() => {
                EditorApplication.update -= callback;
                EditorApplication.update += callback;
                // If we're in running a single method, execute the callback now as
                // EditorApplication.update will not be signaled.
                if (ExecutionEnvironment.ExecuteMethodEnabled) callback();
            });
    }

    /// <summary>
    /// Remove a callback from the EditorApplication.update event.  This is in a method to work
    /// around the mono compiler throwing an exception when this is included inline in the OnUpdate
    /// event.
    /// </summary>
    /// <param name="callback">Callback to remove.</param>
    private static void RemoveOnUpdateCallback(EditorApplication.CallbackFunction callback) {
        Run(() => { EditorApplication.update -= callback; });
    }

    /// <summary>
    /// Run an action setting runningJobs to true before running it and clearing the flag when it's
    /// complete.
    /// </summary>
    /// <param name="action">Action to run.</param>
    private static void RunAction(Action action) {
        runningJobs = true;
        try {
            action();
        } finally {
            runningJobs = false;
        }
    }

    /// <summary>
    /// Poll until a condition is met.
    /// In batch mode this will block and poll until "condition" is met, in non-batch mode the
    /// condition is polled from the main thread.
    /// </summary>
    /// <param name="condition">Method that returns true when the operation is complete, false
    /// otherwise.</param>
    /// <param name="synchronous">Whether to block the calling thread until the condition is met.
    /// </param>
    public static void PollOnUpdateUntilComplete(Func<bool> condition, bool synchronous = false) {
        lock (pollingJobs) {
            pollingJobs.Add(condition);
        }
        if (ExecuteNow && OnMainThread) {
            RunAction(() => {
                    while (true) {
                        ExecuteAllUnnested(true);
                        lock (pollingJobs) {
                            if (pollingJobs.Count == 0) break;
                        }
                        // Wait 100ms.
                        Thread.Sleep(100);
                    }
                });
        } else if (synchronous) {
            while (true) {
                lock (pollingJobs) {
                    if (!pollingJobs.Contains(condition)) break;
                }
                if (OnMainThread) {
                    ExecuteAllUnnested(true);
                } else {
                    // Wait 100ms.
                    Thread.Sleep(100);
                }
            }
        }
    }

    /// <summary>
    /// Execute polling jobs, removing completed jobs from the list.
    /// This method must be called from the main thread.
    /// </summary>
    /// <returns>Number of jobs remaining in the polling job list.</returns>
    private static int ExecutePollingJobs() {
        int numberOfPollingJobs;
        bool completedJobs = false;
        for (int i = 0; /* The exit condition is checked inside the critical section */ ; i++) {
            Func<bool> conditionJob;
            lock (pollingJobs) {
                // If we're at the end of the list because another invocation of this method removed
                // completed jobs, stop executing.
                numberOfPollingJobs = pollingJobs.Count;
                if (i >= numberOfPollingJobs) {
                    break;
                }
                conditionJob = pollingJobs[i];
            }
            bool jobComplete = false;
            try {
                jobComplete = conditionJob();
            } catch (Exception e) {
                jobComplete = true;
                UnityEngine.Debug.LogError(
                    String.Format("Stopped polling job due to exception: {0}",
                                  e.ToString()));
            }
            if (jobComplete) {
                completePollingJobs.Add(conditionJob);
                completedJobs = true;
            }
        }
        if (completedJobs) {
            lock (pollingJobs) {
                foreach (var conditionJob in completePollingJobs) {
                    if (pollingJobs.Remove(conditionJob)) numberOfPollingJobs--;
                }
            }
            completePollingJobs.Clear();
        }

        return numberOfPollingJobs;
    }

    /// <summary>
    /// Schedule a job for execution.
    /// </summary>
    /// <param name="job">Job to execute.</param>
    /// <param name="delayInMilliseconds">Delay before executing the job in milliseconds.</param>
    /// <returns>ID of scheduled job (always non-zero).</returns>
    public static int Schedule(Action job, double delayInMilliseconds) {
        return ScheduledJob.Schedule(job, delayInMilliseconds);
    }

    /// <summary>
    /// Cancel a previously scheduled job.
    /// </summary>
    /// <param name="jobId">ID of previously scheduled job.</param>
    public static void Cancel(int jobId) {
        ScheduledJob.Cancel(jobId);
    }

    /// <summary>
    /// Enqueue a job on the main thread.
    /// In batch mode this must be called from the main thread.
    /// </summary>
    /// <param name="job">Job to execute.</param>
    /// <param name="runNow">Whether to execute this job now if this is the main thread.  The caller
    /// may want to defer execution if this is being executed by InitializeOnLoad where operations
    /// on the asset database may cause Unity to crash.</param>
    public static void Run(Action job, bool runNow = true) {
        bool firstJob;
        lock (jobs) {
            firstJob = jobs.Count == 0;
            jobs.Enqueue(job);
        }
        // If we've just added the first job to the queue, we're on the main thread and the job
        // has been requested to run now, we'll start job execution right now.
        // While pumping the job queue in ExecuteAll(), additional scheduled jobs will be executed
        // as part of the process until the queue is empty.
        // If we're not executing the job right now, the job queue is pumped from the UnityEditor
        // update event.
        if (firstJob && (runNow || ExecuteNow) && OnMainThread) {
            ExecuteAllUnnested(false);
        }
    }

    /// <summary>
    /// Execute the next job on the queue.
    /// </summary>
    private static bool ExecuteNext() {
        Action nextJob = null;
        lock (jobs) {
            if (jobs.Count > 0) nextJob = jobs.Dequeue();
        }
        if (nextJob == null) return false;
        try {
            nextJob();
        } catch (Exception e) {
            UnityEngine.Debug.LogError(String.Format("Job failed with exception: {0}",
                                                     e.ToString()));
        }
        return true;
    }

    /// <summary>
    /// Try executing all scheduled jobs if the caller is on the main thread.
    /// </summary>
    /// <returns>true if the caller is on the main thread, false otherwise.</returns>
    public static bool TryExecuteAll() {
        if (OnMainThread) {
            ExecuteAllUnnested(true);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Execute all scheduled jobs and remove from the update loop if no jobs are remaining.
    /// </summary>
    /// <param name="force">Force execution when a re-entrant call of this method is detected.
    /// This is useful when an application is forcing execution to block the main thread.</param>
    private static void ExecuteAll() {
        ExecuteAllUnnested(false);
    }

    /// <summary>
    /// Execute all scheduled jobs and remove from the update loop if no jobs are remaining.
    /// </summary>
    /// <param name="allowNested">Force execution when a re-entrant call of this method is detected.
    /// This is useful when an application is forcing execution to block the main thread.</param>
    private static void ExecuteAllUnnested(bool allowNested) {
        if (!OnMainThread) {
            UnityEngine.Debug.LogError("ExecuteAll must be executed from the main thread.");
            return;
        }

        // Don't nest job execution on the main thread, return to the last stack frame
        // running ExecuteAll().
        if (runningExecuteAllCount > 0 && !allowNested) return;

        RunAction(() => {
                runningExecuteAllCount ++;
                bool jobsRemaining = true;
                while (jobsRemaining) {
                    jobsRemaining = false;
                    // Execute jobs.
                    while (ExecuteNext()) {
                        jobsRemaining = true;
                    }

                    // Execute polling jobs.
                    int remainingJobs = ExecutePollingJobs();
                    // If we're in running a single method, keep on executing until no polling jobs
                    // remain.
                    if (ExecutionEnvironment.ExecuteMethodEnabled && remainingJobs > 0) {
                        jobsRemaining = true;
                    }
                }
                runningExecuteAllCount --;
            });
    }
}

}
