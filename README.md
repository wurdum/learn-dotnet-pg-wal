# PostgreSQL WAL Outbox Pattern Test

This project is a demonstration of using PostgreSQL's Write-Ahead Logging (WAL) as a foundation for implementing the outbox pattern.

## Overview

The application is built using .NET and leverages Entity Framework Core for database interactions. It includes a background service that simulates life events and stores them in a PostgreSQL database. Another background service listens to the PostgreSQL WAL to capture and log these events.

## Key Components

- **Entity Framework Core**: Used for database interactions and managing the `LifeEvent` entity.
- **PostgreSQL**: Serves as the database, with WAL used for capturing changes.
- **Background Services**: 
  - `Biography`: Simulates life events and stores them in the database.
  - `WalListener`: Listens to the WAL for changes and logs them.

## Running the Project

To run the project, ensure you have Docker installed and execute the following command to start the PostgreSQL service:

```bash
docker-compose up
dotnet run
```

## Links

Base on ideas from:

* [The Wonders of Postgres Logical Decoding Messages](https://www.infoq.com/articles/wonders-of-postgres-logical-decoding-messages/)
* [Implementing the Outbox Pattern with Postgres and .NET](https://www.bytefish.de/blog/outbox_events_postgres_dotnet.html)
* [Blumchen](https://github.com/event-driven-io/Blumchen)
