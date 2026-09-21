# Services

Application and infrastructure services: RSS polling/fetching, the notification abstraction
(`INotificationService`) and its `EmailNotificationService` / `SlackNotificationService`
implementations, email sending (MailKit), and the polling background service. Interfaces live
here alongside their implementations and are registered in DI from `Program.cs`.
