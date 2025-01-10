# OccupancyTracker Monolith Version
The year was 2020 and the pandemic changed many things, including closing all stores. Once stores were opened up, they had strict occupancy rules and there were employees at each door counting the number of people who entered and exited. It occurred to me then that an application to do that would be a fairly simple thing to put together. Fortunately for me, I was kept fully employed during that time so I didn't have time to contemplate it further.

When my job was eliminated in 2024, I realized that this application would be a perferct opportunity for me to learn new skills by implementing it. As I looked at it more closely, however, I realized it could do something else as well, it could work as the perfect example of taking a monolithic codebase and improving it. That's important because the number one rule of any startup should be:

> Getting a product to market, even if it is not fully finished, is a better return then burning money on a product that no one may want.

So I decided to push out a monolithic Blazor server application, using 
* [MudBlazor](https://mudblazor.com/) for the UI
* [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/) for data store 
* [Auth0]([https://www.okta.com/](https://auth0.com/)) for authentication
* [SendGrid] (https://sendgrid.com/en-us) for emails

