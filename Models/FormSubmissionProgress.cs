using System;

namespace knkwebapi_v2.Models
{
    /// <summary>
    /// Tracks a user's progress through a form configuration.
    /// Supports pausing and resuming, including nested sub-forms.
    /// 
    /// PROGRESS LIFECYCLE:
    /// 1. User starts filling out a form → Status = "InProgress"
    /// 2. User clicks "Save Draft" → Status = "Paused", all data saved
    /// 3. User returns later → Load progress, resume at CurrentStepIndex
    /// 4. User completes form → Status = "Completed", CompletedAt set
    /// 5. User abandons form → Status = "Abandoned" (manually set or after timeout)
    /// 
    /// NESTED FORM SUPPORT:
    /// When a user is filling out Shop form and clicks "Create New Domain":
    /// - Current Shop progress is saved with Status = "Paused"
    /// - New Domain progress is created with ParentProgressId = Shop progress ID
    /// - User completes Domain form → Domain progress Status = "Completed"
    /// - Control returns to Shop form → Shop progress Status = "InProgress"
    /// 
    /// This allows unlimited nesting depth: Shop → Domain → Location → etc.
    /// All progress is preserved, so users can pause at any level and resume later.
    /// </summary>
    public class FormSubmissionProgress
    {
        public int Id { get; set; }
        
        /// <summary>
        /// Unique identifier for this progress session.
        /// Used for tracking and analytics (e.g., "How long does it take users to complete this form?").
        /// </summary>
        public Guid ProgressGuid { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// The user filling out this form.
        /// Used to load the user's saved drafts when they return to the application.
        /// </summary>
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        
        /// <summary>
        /// The configuration being filled out.
        /// References the specific FormConfiguration (e.g., "Shop Creation Wizard v2").
        /// </summary>
        public int FormConfigurationId { get; set; }
        public FormConfiguration FormConfiguration { get; set; } = null!;

        /// <summary>
        /// The ID of the entity being edited by this form.
        /// </summary>
        public int? EntityId { get; set; }
        
        /// <summary>
        /// Zero-based index of the step the user is currently on.
        /// Used to resume the wizard at the correct step.
        /// Example: CurrentStepIndex = 2 means the user is on the 3rd step.
        /// </summary>
        public int CurrentStepIndex { get; set; } = 0;
        
        /// <summary>
        /// JSON object storing the field values for the current step.
        /// Format: { "FieldName1": "value1", "FieldName2": "value2", ... }
        /// 
        /// WHY SEPARATE FROM AllStepsDataJson?
        /// - Current step data is frequently updated as the user types.
        /// - AllStepsDataJson only updates when a step is completed.
        /// - This separation improves performance (less data to serialize/deserialize on each save).
        /// 
        /// When user clicks "Next", CurrentStepDataJson is validated, then merged into AllStepsDataJson.
        /// </summary>
        public string CurrentStepDataJson { get; set; } = "{}";
        
        /// <summary>
        /// JSON object storing all completed step data.
        /// Format: { "Step0": { "FieldName1": "value1", ... }, "Step1": { ... }, ... }
        /// 
        /// This is the complete form payload that will be sent to the backend when the user clicks "Submit".
        /// Backend maps this JSON to the target entity using the FieldName properties.
        /// 
        /// EXAMPLE:
        /// {
        ///   "Step0": { "Name": "My Shop", "Description": "A cool shop" },
        ///   "Step1": { "DomainId": 5, "LocationId": 12 },
        ///   "Step2": { "IsPublic": true, "MaxCapacity": 100 }
        /// }
        /// 
        /// Backend extracts these values and creates a Shop entity:
        /// new Shop {
        ///   Name = "My Shop",
        ///   Description = "A cool shop",
        ///   DomainId = 5,
        ///   LocationId = 12,
        ///   IsPublic = true,
        ///   MaxCapacity = 100
        /// }
        /// </summary>
        public string AllStepsDataJson { get; set; } = "{}";
        
        /// <summary>
        /// References the parent progress record if this is a nested sub-form.
        /// NULL for top-level forms.
        /// 
        /// NESTED FORM EXAMPLE:
        /// User is filling out Shop form (Progress A, ParentProgressId = NULL).
        /// User reaches "Domain" field, clicks "Create New Domain".
        /// Domain form opens (Progress B, ParentProgressId = Progress A.Id).
        /// User completes Domain form → Progress B.Status = "Completed".
        /// Control returns to Shop form → Progress A resumes at the same step.
        /// 
        /// This hierarchy allows the system to:
        /// 1. Navigate back to parent form when sub-form completes.
        /// 2. Load all related sub-form data when resuming a paused top-level form.
        /// 3. Track analytics: "How often do users create nested entities vs. selecting existing ones?"
        /// </summary>
        public int? ParentProgressId { get; set; }
        public FormSubmissionProgress? ParentProgress { get; set; }
        
        /// <summary>
        /// Current status of this progress session.
        /// - "InProgress": User is actively filling out the form.
        /// - "Paused": User saved a draft and left (can resume later).
        /// - "Completed": User finished and submitted the form.
        /// - "Abandoned": User did not return within a timeout period (optional cleanup logic).
        /// </summary>
        public string Status { get; set; } = "InProgress";
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Last time this progress was updated (e.g., user saved current step, moved to next step).
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
        
        /// <summary>
        /// Timestamp when the form was completed and submitted.
        /// NULL if Status != "Completed".
        /// Used for analytics and auditing.
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// KNG-162 (decision D2): the entities this form's child forms saved on their own while it
        /// was filled out, as a JSON array written by the web app (entity type, id, a display label,
        /// origin field, creation order). Only set on a root progress (ParentProgressId = NULL).
        /// Lets the "keep or delete what this form created" prompt work when the draft is deleted
        /// later from Saved Progress, and lets a resumed draft pin those entities again. Nothing
        /// deletes these entities automatically; the 90-day cleanup removes only progress rows.
        /// NULL = nothing recorded (every progress saved before this column existed).
        /// </summary>
        public string? CreatedEntitiesJson { get; set; }
    }
}
