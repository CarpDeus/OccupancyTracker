# Help File
Welcome to the Occupancy Tracker (Monolith) Help file. 

## Login / Register
To begin you need to log in or register. Do do that, press the Login/Register button in the main menu:

![RegisterLogin](https://github.com/user-attachments/assets/a61fb28c-5211-4dd0-9f85-91aa9e39b4d1)

This will take you to the Auth0 login screen:

![OktaLogin](https://github.com/user-attachments/assets/b0cf3b31-b1bb-47f7-9c56-3891b526b797)

From here you can either create an acocunt using an email address and password or use a GMail Account to register. Once you have completed that, you will be taken to the profile editor page.

## Profile Editor
The profile editor has the required fields marked. Once the are all filled in, then you have access to the system.

![ProfileEdit](https://github.com/user-attachments/assets/1de55925-41e4-4a5c-be78-6afa5e4a70c7)

## Creating an organization
Once your profile is complete, there are additional options. If you haven't created an organization, you can do that now. While you can belong to multiple organizations, you can only create one. 

![CreateOrganizationButton](https://github.com/user-attachments/assets/6bc2d408-de94-4aed-bd3e-89fbc31f58a5)

Once you've clicked the Create an Organization button, you'll be taken to the organization detail screen. Fill in the form and save it. Once your organization is created, you can add locations.

![OrganizationDetails](https://github.com/user-attachments/assets/4526c583-833f-487f-99b9-cde84c2b331b)

## Adding locations
To add a location, click the Add location button found in the Location List. 

![AddLocationButton](https://github.com/user-attachments/assets/c56c5ea5-80f5-4bcf-bc65-519d5bdc9b9e)

This looks a great deal like the Organizaion details but there are a couple of key differences, most notably the Occupancy fields. There are two of them. Maximum Occupancy is the maximum number
of people allowed in the location. This is generally set by law and enforced by local authorities. Any public location should have that number available to them to know what to set. The second number 
is the Threshhold Warning. When that number is hit, the graphics will change to display that the location is nearing maximum occupancy. If the number of people in the location exceeds the 
maximum occupancy value, then the entrance counters will not allow registering new entrants via the number buttons.

![LocationDetails](https://github.com/user-attachments/assets/29e8f7ee-c8c2-46f2-a0dc-245a11ad7048)

## Adding Entrances
Every location should have one or more entrances.Create entrances by selecting the Add entrance button:

![AddEntrancesButton](https://github.com/user-attachments/assets/b0d2d870-0cd1-411b-9dd2-090fc100a993)

There are only two pieces of data for an entrance, a name and a description.

![CreateEntranceDialog](https://github.com/user-attachments/assets/0a1f48b1-a574-40e7-9753-e3b54209d64d)

These entrances will have entrance counters associated with them so it is possible to track not only how many people entered and exited a building
but also which entrances were used. Once an entrance is created, the counter is automatically created as well and shows up in the list:

![EntranceListWithCounters](https://github.com/user-attachments/assets/07d35e4e-06ad-40c5-9e23-1a1a3f5cbb18)

## Entrance Counters
Clicking on an entrance counter will open up that counter in a new tab. There is no authentication requirement for the entrance counters (though that ability is built in, just not implemented in this 
version). The easiest way to track occupancy is by clicking on the Entering or Leaving buttons.

![EntrranceCounter](https://github.com/user-attachments/assets/9f338310-5973-4b9c-8b72-cf64c577259b)

Pressing 5 under the Entering section will add 5 to the number of occupants. 
### Warning
When the number of occupants reaches or exceeds the threshhold warning limit, then the current occupancy will change color to reflect that the occupancy is in a warning state.

### Exceeded
When the number of occupants exceeds the Maximum Occupancy Limit then the Entering numbers will be disabled until the count goes below the Maximum Occupancy Limit.

![EntranceCounterOverMax](https://github.com/user-attachments/assets/e38813be-80ec-42a3-8ebb-0b17108ef9a8)

## Tracking occupancy
All entrance counters for a location update everytime the current occupany value changes. Additionally, the locations list shows a realtime view of the current occupancy

![LocationListWithOccupancy](https://github.com/user-attachments/assets/5623d71b-7808-432b-992d-f8184493ddaf)
