# Initial design draft

The idea is to build a small, complete solution for the exercise and keep it easy to understand. Before starting, these are the main decisions I would use as a base.

## Stack and structure

I'll use .NET 10 with Minimal APIs for the backend, SQLite with Entity Framework for persistence, and React for the frontend.

The solution should follow Clean Architecture, with clear separation between business rules, application logic, database access, and the API. Keep the structure lightweight; add abstractions only where they help.

## API and validation

Each endpoint should return the appropriate HTTP status code for its outcome. Validate incoming data on the backend and return clear, useful errors. Database failures should be handled properly, without exposing raw database messages to the user.

## Transactions

Transaction correctness is the main thing to get right. Use an `Idempotency-Key` so retrying a request does not create another transaction. Store enough information to recognize a repeated request and handle reuse of a key with different data.

Keep related database changes atomic and account for concurrent requests, so two operations cannot leave the data inconsistent. I'll check retries and simultaneous requests as part of the exercise.

## Frontend

Build a simple React interface that covers the required flows. It should have a clean layout, clear validation, loading states, and useful success and error feedback. Frontend validation should help the user, while the backend remains responsible for enforcing the rules.

## Running and checking the solution

- Add a PowerShell Core script to start the backend and frontend together.
- Add a REST Client `.http` file covering every endpoint, including happy paths, validation errors, and transaction retries.

