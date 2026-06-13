# Ketabino

A book reading and writing platform built with Next.js and ASP.NET Core.

## Prerequisites

- **.NET 8 SDK** - For running the backend
- **Node.js 18+** - For running the frontend
- **SQLite** - Database (automatically created on first run)

---

## Running Without Docker

To run the application locally without Docker, you need to start the backend and frontend in two separate terminal windows.

### Terminal 1: Run the Backend

Open your first terminal window and run:

```bash
# Navigate to the project root
cd ketabino

# Restore dependencies
dotnet restore

# Run the backend (starts on http://localhost:5131)
dotnet run
```

The backend will:

- Create the SQLite database automatically on first run
- Initialize the database schema
- Start listening on `http://localhost:5131`

### Terminal 2: Run the Frontend

Open a **new, separate terminal window** and run:

```bash
# Navigate to the frontend directory (adjust path if needed)
cd ketabino/frontend
# or if you are already in the ketabino root: cd frontend

# Install dependencies
npm install

# Run the development server (starts on http://localhost:3000)
npm run dev
```

### 3. Access the Application

- **Frontend**: http://localhost:3000 (or 3001 if port 3000 is in use)
- **Backend API**: http://localhost:5131
- **Swagger API Docs**: http://localhost:5131/swagger (development only)

### How to use Swagger UI (Admin & Testing)
We have configured **NSwag** so you can easily test the APIs right from your browser.
1. Open your browser and go to [http://localhost:5131/swagger](http://localhost:5131/swagger).
2. To test endpoints that require authentication (like the Admin endpoints or Submitting a Report), you need a token.
3. Use the `POST /api/Auth/login` endpoint to log in (or `POST /api/Auth/register` to create an Admin account). The response will contain a `token`.
4. Copy the `token` string.
5. Click the green **Authorize** button at the top of the Swagger page.
6. Paste your token (no need for the 'Bearer ' prefix, just the token itself) and click **Authorize**.
7. You can now use any protected endpoint directly from the Swagger panel!

---

## Project Structure

```
ketabino/
├── backend/              # ASP.NET Core Web API
│   ├── Controllers/      # API endpoints
│   ├── Database/         # Database connection and schema
│   ├── Models/           # Data models
│   ├── Services/         # Business logic services
│   └── Program.cs        # Application entry point
├── frontend/             # Next.js application
│   ├── app/              # App router pages
│   ├── components/       # React components
│   ├── hooks/            # Custom React hooks
│   ├── lib/              # Utility functions
│   └── types/            # TypeScript types
├── Controllers/          # Root-level controllers (if any)
├── Models/               # Root-level models (if any)
└── Database/             # Root-level database files
```

---

## Environment Variables

### Backend (appsettings.Development.json)

The backend uses default values, but you can customize:

```json
{
  "Jwt": {
    "SecretKey": "YourSecretKey1234567890!@#$",
    "Issuer": "KetabinoApi",
    "Audience": "KetabinoClient"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=ketabino.db"
  }
}
```

### Frontend (.env.local)

Create `frontend/.env.local` if needed:

```env
NEXT_PUBLIC_API_URL=http://localhost:5131
```

---

## Database

SQLite is used by default. The database file (`ketabino.db`) is created automatically in the backend directory on first run.

### Database Schema

The schema is initialized automatically via `SchemaInitializer` on application startup.

---

## API Endpoints

### Authentication

- `POST /api/auth/register` - Register a new user
- `POST /api/auth/login` - Login and get JWT token

### Books

- `GET /api/books` - List all books
- `GET /api/books/{id}` - Get book details
- `POST /api/books` - Create a new book (authenticated)
- `PUT /api/books/{id}` - Update a book (authenticated)
- `DELETE /api/books/{id}` - Delete a book (authenticated)

### Chapters

- `GET /api/chapters/book/{bookId}` - List chapters for a book
- `GET /api/chapters/{id}` - Get chapter details
- `POST /api/chapters` - Create a chapter (authenticated)
- `PUT /api/chapters/{id}` - Update a chapter (authenticated)

### Genres

- `GET /api/genres` - List all genres

### Subscriptions

- `GET /api/subscriptions` - List subscription plans
- `POST /api/subscriptions/subscribe` - Subscribe to a plan (authenticated)

### Wallet

- `GET /api/wallet/balance` - Get user balance (authenticated)
- `POST /api/wallet/deposit` - Deposit funds (authenticated)

### Reading Progress

- `GET /api/reading-progress/{bookId}` - Get reading progress (authenticated)
- `POST /api/reading-progress` - Update reading progress (authenticated)

### Notifications

- `GET /api/notifications` - Get user notifications (authenticated)

### Reports

- `POST /api/reports` - Submit a report (authenticated)

### Admin

- `GET /api/admin/stats` - Get admin statistics (admin only)
- `GET /api/admin/users` - List all users (admin only)

---

## Running with Docker (Alternative)

If you prefer Docker, see [DOCKER.md](./DOCKER.md) for instructions.

---

## Troubleshooting

### CORS Errors

Make sure the backend is running on port 5131 and the frontend on port 3000.

### Database Issues

Delete `ketabino.db` and restart the backend to recreate the database.

### Port Already in Use

- Backend: Change the port in `Properties/launchSettings.json`
- Frontend: Set `PORT=3001` environment variable

