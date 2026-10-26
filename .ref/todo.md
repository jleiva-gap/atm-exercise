------
# Architecture indications
Keep Simple but complete
.NET 10
BD: Sqlite
Orm: Entity Framework
Api: Minimal Api
FrontEnd: React
Clean Architecture
Use the exact http codes for each api endpoint
Error message must be clear and handled properly for the database
Add data validations
Use Idempotency-Key for transactions
We must avoid transactions duplication or concurrency errors
Front end simple, complete and with high validation and a good UX/UI. The UI is not the focus of this but we want a good design.
Create a PowerShell Core start process to start all the sites
Create a http RestClient file to test all the api endpoints (happy path and errors)
