# LuminaFeed — Initial Specification

## Overview

LuminaFeed is an online service that lets users register and sign up for alerts. Users sign up for RSS feeds and receive alerts of the latest items/articles as they come through. Alerts are delivered as notifications, either through email or via Slack.

The UI presents a list of RSS feeds, complete with the images for the feeds. The list of RSS feeds is admin-curated, and all feeds are categorized.

## Users & Roles

- Standard users and admin users must be differentiated. Initially this is a simple flag on the user table called `isAdmin`.
- Globally, users who are not logged in are treated as users who haven't subscribed to anything.

## Categories

- There is an entity listing all available categories. When a new RSS feed is added, its category is selected from a dropdown.
- The initial seeded list of categories should include ones like World News, Markets, Weather, Breaking News, etc.

## Admin Capabilities

Admin users are presented with views to:

- See the list of signed-up users and their RSS feeds.
- Edit the list of categories.
- Edit the list of RSS feeds — delete or add a new one (basically a CRUD).
- Remove certain RSS subscriptions from users, or delete user registrations completely.

## Public UI — Main View

For normal users, the main view displays the RSS feeds by category:

- Each category shows a maximum of 5 entries. On desktop these entries occupy a single row; on mobile, as many as fit.
- At the end of each category's entries there is a **more** button that loads 5 extra.
- **Ordering:** within a category, feeds are ordered by popularity by default. Each category header has an order button; by default it orders by popularity, but it must also be possible to order by name, ascending and descending.
- **Category filter:** at the root (top) of the page it must be possible to filter by category itself. When filtered, only that category's items are shown, capped at 30 rather than 5. If there are still more than 30 items, a more button is shown again, adding another 30 to the cap.
- Feeds are displayed as **cards**. A card also displays the image assigned to the RSS feed, if available.
- Each card has two buttons; one leads the user to that particular RSS feed's page, which lists the latest articles within.

## Feed Page

On a feed's page:

- A different kind of card is used, showing the article's display image, an initial few paragraphs of the article, and the title — all subject to data availability.
- At the top of the page there is again a subscribe button, unless the user is already subscribed.

## Subscribe / Unsubscribe Flow

- When an unauthenticated user tries to subscribe, they are taken to the login page, which also offers to register if they are not registered yet.
- We must keep track of where the user was, so that after they log in or register they return to the original location they started from. Using a query parameter for this is fine.
- If the user is signed in and already subscribed to the particular RSS feed, the button says **unsubscribe** in red.

### Subscribe dialog

When a signed-in user clicks **subscribe**, a dialog window appears with two switches: one for email notifications and one for Slack.

- **Email:** works out of the box because the user is already registered and we have their email address. It is not possible to specify extra or different email addresses, because we must validate the email address by having the user click the verification link. Supporting a different email is out of scope.
- **Slack:** currently a single webhook URL is sufficient to register a Slack notification. When Slack is switched on, an input box is shown for the webhook. The field should be pre-populated if the user has provided a webhook before (`LastOrDefault`). The webhook URL must be validated — we must check it truly is a Slack hook (they begin with `https://hooks.slack.com/services`), otherwise reject it. The webhook has a default channel, so no Slack channel needs to be specified here.

## Notifications Architecture

- Provide an `INotificationService` interface with two implementations: `EmailNotificationService` and `SlackNotificationService`. Depending on the user's choice, invoke either or both.
- Use the **Slack.Webhooks** NuGet package for the `SlackNotificationService`.
- Use **MailKit** for the `EmailNotificationService`; MailKit should also be used when implementing the `EmailSender` for the registration emails.

### Email configuration & templates

- `appsettings.Development.json` should be pre-populated with default Papercut settings for the SMTP server running on localhost.
- Email templates must account for people using Word-based Outlook clients: div-based emails can't be used, so fall back to table-based ones. This also means buttons must be duplicated so they look good in Outlook. These duplicates should only appear for Outlook and not the other clients.

## Feed Polling

- A background task initiates polling every minute. This frequency can be configured in the appsettings file.
- The RSS service keeps track of all feeds the system is subscribed to — i.e. all feeds that at least one user has subscribed to.
- Each time polling runs, iterate through the active feeds and fetch each list. Use the **ETag** and **Last-Modified** headers so servers don't have to return every item; they can return **304 Not Modified** if there is nothing new.
- When articles are returned, the service returns a dictionary keyed by the user's email address, with the value being a list of articles. The background service passes this on to the notification services based on the user's choice.

## Unsubscribe

- The `EmailNotificationService` implements HMAC-signed, token-based unsubscribe, conforming to **RFC 8058**. There is a simple page with a single unsubscribe button to unsubscribe from each of their RSS feeds, accessible through the Unsubscribe link.
- When the unsubscribe button is pressed on the site, it must be possible to unsubscribe from only one of the notification options, or both.

## Technical Conventions

- All new entities created should use **v7 GUIDs** as their IDs.
