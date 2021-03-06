﻿using Microsoft.Graph;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.SharePoint;
using Microsoft.SharePoint.Administration;
using Microsoft.SharePoint.Utilities;
using Microsoft.SharePoint.WebControls;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;
using static azurecp.ClaimsProviderLogging;

namespace azurecp.ControlTemplates
{
    public partial class AzureCPGlobalSettings : AzureCPUserControl
    {
        readonly string TextErrorNewTenantFieldsMissing = "Some mandatory fields are missing.";
        readonly string TextErrorTestAzureADConnection = "Unable to get access token for tenant '{0}': {1}";
        readonly string TextConnectionSuccessful = "Connection successful.";
        readonly string TextErrorNewTenantCreds = "Specify either a client secret or a client certificate, but not both.";

        protected void Page_Load(object sender, EventArgs e)
        {
            Initialize();
        }

        /// <summary>
        /// Initialize controls as needed if prerequisites are ok, otherwise deactivate controls and show error message
        /// </summary>
        protected void Initialize()
        {
            if (ValidatePrerequisite() != ConfigStatus.AllGood)
            {
                this.LabelErrorMessage.Text = base.MostImportantError;
                this.BtnOK.Enabled = false;
                this.BtnOKTop.Enabled = false;
                return;
            }

            PopulateConnectionsGrid();
            if (!this.IsPostBack)
            {
                BuildGraphPropertyDDL();
                PopulateFields();
            }
        }

        void PopulateConnectionsGrid()
        {
            if (PersistedObject.AzureTenants != null)
            {
                PropertyCollectionBinder pcb = new PropertyCollectionBinder();
                foreach (AzureTenant tenant in PersistedObject.AzureTenants)
                {
                    pcb.AddRow(tenant.Identifier, tenant.Name, tenant.ApplicationId, tenant.ExcludeMembers);
                }
                pcb.BindGrid(grdAzureTenants);
            }
        }

        private void PopulateFields()
        {
            if (IdentityCTConfig.DirectoryObjectPropertyToShowAsDisplayText == AzureADObjectProperty.NotSet)
            {
                this.RbIdentityDefault.Checked = true;
            }
            else
            {
                this.RbIdentityCustomGraphProperty.Checked = true;
                this.DDLGraphPropertyToDisplay.Items.FindByValue(((int)IdentityCTConfig.DirectoryObjectPropertyToShowAsDisplayText).ToString()).Selected = true;
            }
            this.DDLDirectoryPropertyMemberUsers.Items.FindByValue(((int)IdentityCTConfig.DirectoryObjectProperty).ToString()).Selected = true;
            this.DDLDirectoryPropertyGuestUsers.Items.FindByValue(((int)IdentityCTConfig.DirectoryObjectPropertyForGuestUsers).ToString()).Selected = true;
            this.ChkAlwaysResolveUserInput.Checked = PersistedObject.AlwaysResolveUserInput;
            this.ChkFilterExactMatchOnly.Checked = PersistedObject.FilterExactMatchOnly;
            this.ChkAugmentAADRoles.Checked = PersistedObject.EnableAugmentation;
            this.ChkFilterSecurityEnabledGroupsOnly.Checked = PersistedObject.FilterSecurityEnabledGroupsOnly;
        }

        private void BuildGraphPropertyDDL()
        {
            AzureADObjectProperty[] aadPropValues = (AzureADObjectProperty[])Enum.GetValues(typeof(AzureADObjectProperty));
            IEnumerable<AzureADObjectProperty> aadPropValuesSorted = aadPropValues.OrderBy(v => v.ToString());
            foreach (AzureADObjectProperty prop in aadPropValuesSorted)
            {
                // Ensure property exists for the User object type
                if (AzureCP.GetPropertyValue(new User(), prop.ToString()) == null) { continue; }

                // Ensure property is of type System.String
                PropertyInfo pi = typeof(User).GetProperty(prop.ToString());
                if (pi == null) { continue; }
                if (pi.PropertyType != typeof(System.String)) { continue; }

                this.DDLGraphPropertyToDisplay.Items.Add(new System.Web.UI.WebControls.ListItem(prop.ToString(), ((int)prop).ToString()));
                this.DDLDirectoryPropertyMemberUsers.Items.Add(new System.Web.UI.WebControls.ListItem(prop.ToString(), ((int)prop).ToString()));
                this.DDLDirectoryPropertyGuestUsers.Items.Add(new System.Web.UI.WebControls.ListItem(prop.ToString(), ((int)prop).ToString()));
            }
        }

        protected void grdAzureTenants_RowDeleting(object sender, GridViewDeleteEventArgs e)
        {
            if (ValidatePrerequisite() != ConfigStatus.AllGood) { return; }
            if (PersistedObject.AzureTenants == null) { return; }

            GridViewRow rowToDelete = grdAzureTenants.Rows[e.RowIndex];
            Guid Id = new Guid(rowToDelete.Cells[0].Text);
            AzureTenant tenantToRemove = PersistedObject.AzureTenants.FirstOrDefault(x => x.Identifier == Id);
            if (tenantToRemove != null)
            {
                PersistedObject.AzureTenants.Remove(tenantToRemove);
                CommitChanges();
                ClaimsProviderLogging.Log($"Azure AD tenant '{tenantToRemove.Name}' was successfully removed from configuration '{PersistedObjectName}'", TraceSeverity.Medium, EventSeverity.Information, TraceCategory.Configuration);
                PopulateConnectionsGrid();
            }
        }

        protected bool UpdateConfiguration(bool commitChanges)
        {
            if (ValidatePrerequisite() != ConfigStatus.AllGood) { return false; }

            if (this.RbIdentityCustomGraphProperty.Checked)
            {
                IdentityCTConfig.DirectoryObjectPropertyToShowAsDisplayText = (AzureADObjectProperty)Convert.ToInt32(this.DDLGraphPropertyToDisplay.SelectedValue);
            }
            else
            {
                IdentityCTConfig.DirectoryObjectPropertyToShowAsDisplayText = AzureADObjectProperty.NotSet;
            }

            AzureADObjectProperty newUserIdentifier = (AzureADObjectProperty)Convert.ToInt32(this.DDLDirectoryPropertyMemberUsers.SelectedValue);
            if (newUserIdentifier != AzureADObjectProperty.NotSet)
            {
                PersistedObject.ClaimTypes.UpdateUserIdentifier(newUserIdentifier);
            }

            AzureADObjectProperty newIdentifierForGuestUsers = (AzureADObjectProperty)Convert.ToInt32(this.DDLDirectoryPropertyGuestUsers.SelectedValue);
            if (newIdentifierForGuestUsers != AzureADObjectProperty.NotSet)
            {
                PersistedObject.ClaimTypes.UpdateIdentifierForGuestUsers(newIdentifierForGuestUsers);
            }

            PersistedObject.AlwaysResolveUserInput = this.ChkAlwaysResolveUserInput.Checked;
            PersistedObject.FilterExactMatchOnly = this.ChkFilterExactMatchOnly.Checked;
            PersistedObject.EnableAugmentation = this.ChkAugmentAADRoles.Checked;
            PersistedObject.FilterSecurityEnabledGroupsOnly = this.ChkFilterSecurityEnabledGroupsOnly.Checked;

            if (commitChanges) { CommitChanges(); }
            return true;
        }

        protected void BtnTestAzureTenantConnection_Click(Object sender, EventArgs e)
        {
            this.ValidateAzureTenantConnection();
        }

        protected void ValidateAzureTenantConnection()
        {
            if (String.IsNullOrWhiteSpace(this.TxtTenantName.Text) || String.IsNullOrWhiteSpace(this.TxtClientId.Text))
            {
                this.LabelErrorTestLdapConnection.Text = TextErrorNewTenantFieldsMissing;
                return;
            }

            if ((InputClientCertFile.PostedFile == null && String.IsNullOrWhiteSpace(this.TxtClientSecret.Text)) ||
                (InputClientCertFile.PostedFile != null && InputClientCertFile.PostedFile.ContentLength == 0 && String.IsNullOrWhiteSpace(TxtClientSecret.Text)) ||
                (InputClientCertFile.PostedFile != null && InputClientCertFile.PostedFile.ContentLength != 0 && !String.IsNullOrWhiteSpace(TxtClientSecret.Text)))
            {
                this.LabelErrorTestLdapConnection.Text = TextErrorNewTenantCreds;
                return;
            }

            string tenantName = this.TxtTenantName.Text;
            string clientId = this.TxtClientId.Text;
            string clientSecret = this.TxtClientSecret.Text;
            // The whole flow of setting the certificate and testing it in AADAppOnlyAuthenticationProvider needs to be done as app pool account
            // Otherwise AADAppOnlyAuthenticationProvider throws CryptographicException: Keyset does not exist (which means it could not access the private key) 
            SPSecurity.RunWithElevatedPrivileges(delegate ()
            {
                X509Certificate2 cert = null;
                if (String.IsNullOrWhiteSpace(this.TxtClientSecret.Text))
                {
                    if (ValidateUploadedCertFile(InputClientCertFile, this.InputClientCertPassword.Text, out cert) == false)
                    { return; }
                }
                try
                {
                    AADAppOnlyAuthenticationProvider testConnection;
                    if (String.IsNullOrWhiteSpace(this.TxtClientSecret.Text))
                    {
                        testConnection = new AADAppOnlyAuthenticationProvider(ClaimsProviderConstants.AuthorityUriTemplate, tenantName, clientId, cert, String.Empty, ClaimsProviderConstants.DEFAULT_TIMEOUT);
                    }
                    else
                    {
                        testConnection = new AADAppOnlyAuthenticationProvider(ClaimsProviderConstants.AuthorityUriTemplate, tenantName, clientId, clientSecret, String.Empty, ClaimsProviderConstants.DEFAULT_TIMEOUT);
                    }
                    Task<bool> testConnectionTask = testConnection.GetAccessToken(true);
                    testConnectionTask.Wait();
                    this.LabelTestTenantConnectionOK.Text = TextConnectionSuccessful;
                }
                catch (AdalServiceException ex)
                {
                    this.LabelErrorTestLdapConnection.Text = String.Format(TextErrorTestAzureADConnection, tenantName, ex.Message);
                }
                catch (Exception ex)
                {
                    this.LabelErrorTestLdapConnection.Text = String.Format(TextErrorTestAzureADConnection, tenantName, ex.Message);
                }
            });
        }

        protected void BtnOK_Click(Object sender, EventArgs e)
        {
            if (ValidatePrerequisite() != ConfigStatus.AllGood) { return; }
            if (UpdateConfiguration(true))
            {
                Response.Redirect("/Security.aspx", false);
            }
            else
            {
                LabelErrorMessage.Text = base.MostImportantError;
            }
        }

        protected void BtnResetAzureCPConfig_Click(Object sender, EventArgs e)
        {
            AzureCPConfig.DeleteConfiguration(PersistedObjectName);
            Response.Redirect(Request.RawUrl, false);
        }

        protected void BtnAddAzureTenant_Click(object sender, EventArgs e)
        {
            AddTenantConnection();
        }

        /// <summary>
        /// Add new Azure AD tenant in persisted object
        /// </summary>
        void AddTenantConnection()
        {
            if (ValidatePrerequisite() != ConfigStatus.AllGood) { return; }
            if (String.IsNullOrWhiteSpace(this.TxtTenantName.Text) || String.IsNullOrWhiteSpace(this.TxtClientId.Text))
            {
                this.LabelErrorTestLdapConnection.Text = TextErrorNewTenantFieldsMissing;
                return;
            }

            if ((InputClientCertFile.PostedFile == null && String.IsNullOrWhiteSpace(this.TxtClientSecret.Text)) ||
                (InputClientCertFile.PostedFile != null && InputClientCertFile.PostedFile.ContentLength == 0 && String.IsNullOrWhiteSpace(TxtClientSecret.Text)) ||
                (InputClientCertFile.PostedFile != null && InputClientCertFile.PostedFile.ContentLength != 0 && !String.IsNullOrWhiteSpace(TxtClientSecret.Text)))
            {
                this.LabelErrorTestLdapConnection.Text = TextErrorNewTenantCreds;
                return;
            }

            X509Certificate2 cert = null;
            if (String.IsNullOrWhiteSpace(this.TxtClientSecret.Text))
            {
                if (ValidateUploadedCertFile(InputClientCertFile, this.InputClientCertPassword.Text, out cert) == false)
                { return; }
            }

            if (PersistedObject.AzureTenants == null)
            {
                PersistedObject.AzureTenants = new List<AzureTenant>();
            }
            this.PersistedObject.AzureTenants.Add(
                new AzureTenant
                {
                    Name = this.TxtTenantName.Text,
                    ApplicationId = this.TxtClientId.Text,
                    ApplicationSecret = this.TxtClientSecret.Text,
                    ExcludeMembers = this.ChkMemberUserTypeOnly.Checked,
                    ClientCertificatePrivateKey = cert
                });

            CommitChanges();
            ClaimsProviderLogging.Log($"Azure AD tenant '{this.TxtTenantName.Text}' was successfully added in configuration '{PersistedObjectName}'", TraceSeverity.Medium, EventSeverity.Information, TraceCategory.Configuration);

            PopulateConnectionsGrid();
            this.TxtTenantName.Text = "TENANTNAME.onMicrosoft.com";
            this.TxtClientId.Text = String.Empty;
            this.TxtClientSecret.Text = String.Empty;
            this.InputClientCertPassword.Text = String.Empty;
        }

        private bool ValidateUploadedCertFile(
            HtmlInputFile inputFile,
            string certificatePassword,
            out X509Certificate2 cert)
        {
            cert = null;
            if (inputFile.PostedFile == null ||
                inputFile.PostedFile.ContentLength == 0)
            {
                this.LabelErrorTestLdapConnection.Text = $"No certificate was passed.";
                return false;
            }

            // Ensure that fileName is just the file name (no directories), then check that fileName is legal.
            string fileName = string.Empty;
            try
            {
                fileName = Path.GetFileName(inputFile.PostedFile.FileName);
            }
            catch (ArgumentException ex)
            {
                this.LabelErrorTestLdapConnection.Text = $"Invalid file path. Error message: {ex.Message}";
                return false;
            }
            if (!SPUrlUtility.IsLegalFileName(fileName))
            {
                this.LabelErrorTestLdapConnection.Text = $"The file name is not legal.";
                return false;
            }

            try
            {
                byte[] buffer = new byte[inputFile.PostedFile.ContentLength];
                inputFile.PostedFile.InputStream.Read(buffer, 0, buffer.Length);
                cert = new X509Certificate2(buffer, certificatePassword, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
                if (cert.HasPrivateKey == false)
                {
                    this.LabelErrorTestLdapConnection.Text = $"Certificate does not contain the private key.";
                    return false;
                }

                // Try to export the certificate with its private key to validate that it succeeds
                cert.Export(X509ContentType.Pkcs12, "Yvan");
            }
            catch (CryptographicException ex)
            {
                this.LabelErrorTestLdapConnection.Text = $"Invalid certificate. Error message: {ex.Message}";
                return false;
            }
            return true;
        }
    }

    public class PropertyCollectionBinder
    {
        protected DataTable PropertyCollection = new DataTable();
        public PropertyCollectionBinder()
        {
            PropertyCollection.Columns.Add("Id", typeof(Guid));
            PropertyCollection.Columns.Add("TenantName", typeof(string));
            PropertyCollection.Columns.Add("ClientID", typeof(string));
            PropertyCollection.Columns.Add("MemberUserTypeOnly", typeof(bool));
        }

        public void AddRow(Guid Id, string TenantName, string ClientID, bool MemberUserTypeOnly)
        {
            DataRow newRow = PropertyCollection.Rows.Add();
            newRow["Id"] = Id;
            newRow["TenantName"] = TenantName;
            newRow["ClientID"] = ClientID;
            newRow["MemberUserTypeOnly"] = MemberUserTypeOnly;
        }

        public void BindGrid(SPGridView grid)
        {
            grid.DataSource = PropertyCollection.DefaultView;
            grid.DataBind();
        }
    }
}
