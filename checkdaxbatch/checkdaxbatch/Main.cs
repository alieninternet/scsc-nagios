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

namespace SCSCNagios.CheckDaxBatch
{
    /*
     * Main program (includes entrypoint)
     */
    class Program
    {
        /*
         * Print the usage information
         */
        static void printUsage()
        {
            Console.WriteLine("checkdaxbatch (20070217)\n");
            Console.WriteLine(Resources.ResourceManager.GetString("Usage"));
        }


        /*
         * Executable entry point (console)
         */
        static void Main(string[] args)
        {
            string className = null;
            string company = null;
            string username = null;
            string password = null;
            string domain = null;
            long warningSecs = 0;
            long criticalSecs = 0;
            
            // We need at least one argument..
            if (args.Length < 3) {
                printUsage();
                Environment.ExitCode = -1;
                return;
            }

            // Try and grab arguments..
            className = args[0];
            try {
                warningSecs = Convert.ToInt64((string)args[1]);
                criticalSecs = Convert.ToInt64((string)args[2]);
            } catch (FormatException) {
                Console.WriteLine("Warning and Critical values must be numbers. Run with no parameters for help.");
                Environment.Exit(-1);
                return;
            }

            // Make sure the numbers are OK
            if ((warningSecs != 0) &&
                (criticalSecs != 0) &&
                (criticalSecs <= warningSecs)) {
                Console.WriteLine("Critical threshold must be longer than the warning threshold.");
                Environment.Exit(-1);
                return;
            }

            // Create our batch checker..
            BatchChecker checker =
                new BatchChecker(ref className, 
                                 ref username,
                                 ref password,
                                 ref domain,
                                 ref company);

            // Perform the check..
            checker.check(warningSecs, criticalSecs);

            // Print the result..
            Console.WriteLine(checker.getNagiosMessage());

            // Return the result (status level)..
            Environment.Exit((int)checker.getNagiosStatus());
        }
    }
}
