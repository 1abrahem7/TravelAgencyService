 ibrahim azbarga 325370500 
abed alrhman abu asabeh 324277581


# Travel Agency Service – MVC Web Application

## 📌 Project Description
This project is a **Travel Agency Service** web application developed as part of the  
**Introduction to Computer Communications** course.

The system allows users to search for trips, make bookings, manage payments, and view their reservations,  
while administrators can manage trips, bookings, and system data.

The project was developed using the **MVC architecture**, as required by the course instructions.

---

## 🛠 Technologies Used
- ASP.NET Core MVC  
- Entity Framework Core  
- SQL Server  
- C#  
- Razor Views (HTML/CSS)  
- Visual Studio / Cursor  

---

## 👥 User Roles
### 👤 Regular User
- Register and log in
- Search for available trips
- Book trips
- View personal bookings
- Simulate payment process

### 🛡 Admin
- Manage trips (add / edit / delete)
- View and manage bookings
- Manage users

---

## 📂 Project Structure
- **Models** – Database entities (Trip, Booking, User, etc.)
- **Views** – Razor pages for UI
- **Controllers** – Application logic and request handling
- **Data** – Database context and migrations

---

## 🔗 Git Repository
The project source code is available at:  
👉 **[INSERT YOUR GIT REPOSITORY LINK HERE]**

Example:  
https://github.com/username/TravelAgencyService

---

## 🗄 Database Setup

### 1️⃣ Create Database
Create a new SQL Server database (e.g. `TravelAgencyDB`).

### 2️⃣ Tables Creation Script
Run the following SQL script to create the required tables:

```sql
CREATE TABLE Users (
    Id INT IDENTITY PRIMARY KEY,
    UserName NVARCHAR(50) NOT NULL,
    Email NVARCHAR(100) NOT NULL,
    PasswordHash NVARCHAR(255) NOT NULL,
    Role NVARCHAR(20) NOT NULL
);

CREATE TABLE Trips (
    Id INT IDENTITY PRIMARY KEY,
    Destination NVARCHAR(100) NOT NULL,
    StartDate DATE NOT NULL,
    EndDate DATE NOT NULL,
    Price DECIMAL(10,2) NOT NULL,
    AvailableSeats INT NOT NULL
);

CREATE TABLE Bookings (
    Id INT IDENTITY PRIMARY KEY,
    UserId INT NOT NULL,
    TripId INT NOT NULL,
    BookingDate DATETIME NOT NULL,
    PaymentReference NVARCHAR(50),
    FOREIGN KEY (UserId) REFERENCES Users(Id),
    FOREIGN KEY (TripId) REFERENCES Trips(Id)
);
