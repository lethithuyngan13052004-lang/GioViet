# Trip Management Refinement: Fixes, Locks, and Notifications

This plan addresses a critical bug in trip station visibility and implements advanced business logic for journey safety, capacity management, and automated passenger communication (counting Accepted and Shipping orders).

## User Review Required

> [!IMPORTANT]
> - **Journey Safety (Status 1/2)**: 
>   - **InProgress (Status 1)**: Key trip details (Vehicle, Main Stations, Time, Capacity, Price) will be locked. You can ONLY modify intermediate stations (Trạm phụ). 
>   - **Completed (Status 2)**: The trip is archived. All editing is disabled, and the "Update" button is hidden.
> - **Capacity Integrity**: The system will prevent you from reducing the trip's capacity below the total weight of **Accepted** and **Shipping** orders already assigned to this trip.
> - **Automated Notifications**: If you change the **StartTime** of a trip, all passengers will automatically receive a notification in their respective group chats (sent by the AI/System bot).

## Proposed Changes

### UI & UX Logic
#### [MODIFY] [EditTrip.cshtml](file:///c:/Users/Asus/source/repos/GioViet/TimChuyenDi/Views/Driver/EditTrip.cshtml)
- **Edit Restrictions**:
  - Implement a `readonly` / `disabled` overlay based on `Model.Status`.
  - **Status 1**: Disable all inputs except for the "Intermediate Stations" (Trạm phụ) section.
  - **Status 2**: Hide the "Cập nhật" button and disable everything.
- **Capacity Validation (JS)**:
  - Calculate `currentTotalWeight` (sum of weights for orders with Status 1 or 3).
  - Add real-time validation to the `AvaiCapacityKg` input to prevent saving values below `currentTotalWeight`.
- **Bug Fix**:
  - Fix the rendering logic of `stopsList` to ensure intermediate stations are visible on load if they exist.

### Business Logic & Notifications
#### [MODIFY] [DriverController.cs](file:///c:/Users/Asus/source/repos/GioViet/TimChuyenDi/Controllers/DriverController.cs)
- **Validation**:
  - Add backend check to ensure `updatedTrip.AvaiCapacityKg` >= total weight of existing **Accepted/Shipping** orders.
- **Notifications**:
  - Detect changes in `StartTime`.
  - If changed, fetch all `Shiprequests` with active statuses (1: Accepted, 3: Shipping).
  - For each request, insert a new `Chatmessage` record using the bot role.

## Open Questions

- N/A

## Verification Plan

### Automated Tests
- N/A

### Manual Verification
1. **Bug Fix**: Verify intermediate stations appear correctly.
2. **Locking**: Verify Status 1 and Status 2 UI restrictions are active.
3. **Capacity**: Try to reduce capacity below existing demand (Accepted/Shipping); verify the error message.
4. **Notifications**: Change the start time; verify messages appear in the database for relevant chat sessions.
