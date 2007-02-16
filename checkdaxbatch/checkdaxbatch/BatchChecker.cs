/* Nagios plugin to check recurring batch execution within Dynamics Ax
 * Copyright (c) 2007 Simon Butcher <simon@butcher.name>
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using Microsoft.Dynamics.BusinessConnectorNet;

namespace SCSCNagios.CheckDaxBatch
{
    /*
     * Axapta checking thingy
     */
    class BatchChecker {
        // Batch status enum (same as within the AOT)
        protected enum BatchStatusEnum : int
        {
            Hold = 0,
            Waiting = 1,
            Executing = 2,
            Error = 3,
            Finished = 4
        };

        // Nagios/NRPE return codes
        public enum NagiosStatusEnum : short
        {
            Unknown = -1,
            Ok = 0,
            Warning = 1,
            Critical = 2
        };

        // Login details..
        protected string username = null;
        protected string password = null;
        protected string domain = null;
        protected string company = null;

        // The class details..
        protected string batchClassName = "SS_SupportImportEmailMAPI";

        // Details from Axapta
        protected DateTime startTime;
        protected DateTime endTime;
        protected BatchStatusEnum batchStatus;

        // Stuff to return for Nagios/NRPE
        protected string nagiosMessage = null;
        protected NagiosStatusEnum nagiosStatus = NagiosStatusEnum.Ok;


        /* 
         * Constructor
         */
        public BatchChecker(ref string _batchClassName,
                            ref string _username,
                            ref string _password,
                            ref string _domain,
                            ref string _company)
        {
            // Copy stuff over
            batchClassName = _batchClassName;
            username = _username;
            password = _password;
            domain = _domain;
            company = _company;

            // Sanity check.
            if (batchClassName == null) {
                throw new ArgumentNullException("_company", 
                                                "Precognition is currently unsupported.");
            }
        }


        /*
         * Return the message for Nagios
         */
        public string getNagiosMessage()
        {
            return nagiosMessage;
        }


        /*
         * Return the status level for Nagios
         */
        public NagiosStatusEnum getNagiosStatus()
        {
            return nagiosStatus;
        }


        /*
         * Grab a string representing the nagios status
         */
        private string getNagiosStatusString()
        {
            switch (this.getNagiosStatus()) {
                case NagiosStatusEnum.Ok:
                    return "OK";

                case NagiosStatusEnum.Warning:
                    return "Warning";

                case NagiosStatusEnum.Critical:
                    return "Critical";
            }

            // Everything else is unknown
            return "Unknown";
        }


        /*
         * Return the batch status as a string
         */
        private string getBatchStatusString()
        {
            switch (this.batchStatus) {
                case BatchStatusEnum.Hold:
                    return "Withheld";

                case BatchStatusEnum.Waiting:
                    return "Waiting";

                case BatchStatusEnum.Executing:
                    return "Executing";

                case BatchStatusEnum.Error:
                    return "Error";

                case BatchStatusEnum.Finished:
                    return "Executed";
            }

            // Weird, and we don't know..
            return "Unknown";
        }


        /*
         * Save an exception and abort
         */
        private void handleException(Exception exception)
        {
            Exception innerException = exception.InnerException;

            // Do we have an inner exception that also needs to be described?
            if (innerException != null)
            {
                nagiosMessage = 
                    String.Format("Error: {0:S} ({1:S})",
                                  exception.Message,
                                  innerException.Message);
            } else {
                nagiosMessage = 
                    String.Format("Error: {0:S}",
                                  exception.Message);
            }

            // We have no idea of the status..
            nagiosStatus = NagiosStatusEnum.Unknown;
        }


        /*
         * Connect to Dynamics Ax and pull back data
         */ 
        private void grabAxBatchData()
        {
            // Create our connection to Ax
            Axapta ax = new Axapta();

            /* Log into axapta using (mostly) default configuration details.
             * Note, in this case, I'm checking to see if we were given details
             * such as who to log in as. If we don't get that sort of information,
             * then we'll just log in the "normal" way..
             */
            if ((username == null) ||
                (password == null)) {
                ax.Logon(company, null, null, null);
            } else {
                ax.LogonAs(username, domain,
                           new NetworkCredential(username, password, domain),
                           company, null, null, null);
            }

            // Find out the class number of the given batch name
            int batchClassNum =
                (int)ax.CallStaticClassMethod("Global", "className2Id", batchClassName);

            // Make sure we have a real class number here..
            if (batchClassNum > 0)
            {
                // Let's grab a cursor to the batch table
                AxaptaRecord batchTable =
                    ax.CreateAxaptaRecord("Batch");

                // Let's grab the most recent detail about this class..
                batchTable.ExecuteStmt(String.Format("select firstonly StartDate,StartTime,EndDate,EndTime,Status " +
                                                     "from axTbl_0 " +
                                                     "order by EndDate desc, EndTime desc " +
                                                     "where ((axTbl_0.ClassNum == {0:D}) && " +
                                                     "       ((axTbl_0.Status != BatchStatus::Waiting) || " +
                                                     "        (axTbl_0.Status != BatchStatus::Executing)))",
                                                     batchClassNum));

                // Convert the date/time fields
                startTime = (DateTime)batchTable.get_Field("StartDate");
                startTime = startTime.AddSeconds((int)batchTable.get_Field("StartTime"));
                endTime = (DateTime)batchTable.get_Field("EndDate");
                endTime = endTime.AddSeconds((int)batchTable.get_Field("EndTime"));

                // Grab the status value
                batchStatus =
                    (BatchStatusEnum)batchTable.get_Field("Status");
            } else {
                nagiosMessage = String.Format("Class '{0:S}' not found!",
                                              batchClassName);
                nagiosStatus = NagiosStatusEnum.Unknown;
            }

            // Finally, log off from Axapta
            ax.Logoff();
        }


        /*
         * Perform the check
         */
        public void check(long warningSecs,
                          long criticalSecs)
        {
            // Grab data from Axapta..
            try {
                this.grabAxBatchData();
            } catch (Exception exception) {
                this.handleException(exception);
            }

            // A string for the message detail
            string messageDetail = null;

            // Check the batch status - it could be bad.
            switch (batchStatus) {
                // Held batch jobs are put on warning..
                case BatchStatusEnum.Hold:
                    nagiosStatus = NagiosStatusEnum.Warning;
                    break;

                // Batch jobs that ended in disaster are obviously critical
                case BatchStatusEnum.Error:
                    nagiosStatus = NagiosStatusEnum.Critical;
                    break;

                // Batch jobs that finished are OK, but we might want to look closer..
                case BatchStatusEnum.Finished:
                    // It's ok for now..
                    nagiosStatus = NagiosStatusEnum.Ok;
                    messageDetail =
                        String.Format("Started {0}, Ended {1}",
                                      startTime,
                                      endTime);

                    // Do we need to check the critical threshold?
                    if (criticalSecs> 0) {
                        // What's the time Mr. Wolf?
                        DateTime timeNow = DateTime.Now;
                        TimeSpan timeDelta = timeNow.Subtract(endTime);

                        if (endTime.AddSeconds(criticalSecs) <
                            timeNow) {
                            nagiosStatus = NagiosStatusEnum.Critical;
                        } else if (endTime.AddSeconds(warningSecs) <
                                   timeNow) {
                            nagiosStatus = NagiosStatusEnum.Warning;
                        }

                        messageDetail +=
                            String.Format(", last ran {0} ago",
                                          timeNow.Subtract(endTime));
                    }
                    break;

                // For everything else, we're confused.
                default:
                    nagiosStatus = NagiosStatusEnum.Unknown;
                    messageDetail = 
                        String.Format("Confused about batch status of {0:D}",
                                      (int)batchStatus);
                    break;
            }

            // Start building the message for nagios..
            nagiosMessage =
                String.Format("Batch class '{0:S}' is {1:S} ({2:S})",
                              batchClassName,
                              this.getNagiosStatusString(),
                              this.getBatchStatusString());

            // Do we have additional detail?
            if (messageDetail != null) {
                nagiosMessage += ": " + messageDetail;
            }
        }
    }
}
