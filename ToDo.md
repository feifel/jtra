# TODO
## Rename Pages
1. Rename Home.razor to Worklog.razor
2. Rename TaskTypes.razor to Tasks.razor
3. Rename TicketsView.razor to Tickets.razor
4. Rename SettingsView.razor to Settings.razor

## Rename and move Dialogs
1. Rename EditEntryDialog.razor to Worklog.razer and move it to the Dialogs folder
2. Rename EditTaskDialog.razor to Task.razer and move it to the Dialogs folder

## Worklog
1. Add Title and buttons "Add" and "..." similar to Tickets and Tasks page
2. Remove inlinle-add-row and replace it by moving today-stats to that place
3. Make sure the EditWorklogDialog has Title "Edit Worklog" or "Add Worklog" depending on the action
4. When the user tries to add a new entry that is exactly like the one before except the time, then ignore this entry, since it is not needed to have 2 entries for the same task
5. 

## Tickets
1. To add/remove Tickets, implement a Ticket.razor in the Dialog folder
2. Use the same Layout and logic like we have in Tasks.razor

## Tasks
1. Implement the follwing 3 default tasks that are hard coded and displayed in the top of the Tasks list:
    1. Category: Break
    2. Category: Holiday SubCategory: Public
    3. Category: Holiday SubCategory: Private

## Charts
1. Implement a Charts Page that shows a Radzen Sunburst Chart to show the Category and SubCategory over a time range
2. Implement a filter line on top of the page to select the worklog items to be displayed in the Chart:
    1. Label (used as Chart Title)
    1. A Calender button that shows the following predefined ranges:
        1. Today
        2. Yesterday
        3. Last 7 Days
        3. This Week
        4. Last Week
        5. Last 2 Weeks
        6. This Month
        7. Last Month
        8. This Year
        9. Last Year
    2. From DateTime 
    3. To DateTime
    4. Excluded Tasks (default exclusoin: Break and Public Holiday)

## Reports
1. Implement a Report Page that let you select a month and then shows for every day of that month the following numbers:
    1. The expected hours for that day (8h for MO-FR)
    2. Reported hours in HH:mm
    3. The Deviation beween expected and reported hours in +-HH:mm
       -> show positive number if expected < reported
    4. Reported hours in fractions of a 8h day e.g. 0.5 for 4h.
    5. The Deviation in fractions of a 8h day
2. In the bottom of the page also show the total of that month for:
    1. Expected Hours / Reported Hours
    2. Expected Days / Reported Days + Deviation
3. Make sure that Worklog entries for Public Holidays are removed from the Expected hours/days and the Reported hours/days.

## Settings
1. Add a readonly e-mail field shown on top of the settings
2. Make the Personal Access Token Readonly and add a button to configure it in a pupup with the following features:
    1. Instructions how to generate it in jira
    2. The field to enter the Access Token 
    3. Add a button to save and close the dialog
    4. When the user press the save button, use the JIRA API (GET /rest/api/2/myself) to get the emailAddress from JIRA and store it in the e-mail field (Note: the JIRA API requests need to be sent via Server, to avaoid CORS issues)
2. Remove "Max Snooze Hours", since it should not be limited
3. Implement a backup timer that triggers a task that creates a JSON file of all the data in the Browser and Sends it to the server, that stores it in a file with name user-email.json in a folder that is configurable on server side.
4. Also add a button to trigger the backup manually


## Help 
1. Add a help page that contains a description of this app