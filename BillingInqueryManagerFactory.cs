using HMIS.Core.Exception.Utilities;
using HMIS.Core.Log;
using HMIS.Core.Log.Enums;
using HMIS.Shared.Model.Models.Billing;
using HMIS.Shared.Model.Models.MPI;
using HMIS.Shared.Model.Models.OPD;
using HMIS.VM.BL.BusinessFlow.BusinessRule.Interfaces;
using HMIS.VM.BL.BusinessFlow.DataFlow.Interfaces;
using HMIS.VM.BL.BusinessFlow.Managers.Interfaces;
using HMIS.VM.BL.BusinessFlow.ManagersFactory.Interfaces;
using HMIS.VM.BL.Data.CustomModel;
using HMIS.VM.BL.Data.Interfaces;
using HMIS.VM.Enums;
using HMIS.VM.Model;
using HMIS.VM.Model.Custom;
using HMIS.VM.Model.UnitOfWorks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Transactions;
using VM.Support;

namespace HMIS.VM.BL.BusinessFlow.ManagersFactory
{
    public class BillingInqueryManagerFactory : IBillingInqueryManagerFactory
    {
        private readonly IVMUnitOfWork vMUnitOfWork;
        private readonly IBillingInqueryManager billingInqueryManager;
        private readonly IVisitServiceManager visitServiceManager;
        private readonly IVisitNoteManager visitNoteManager;
        private readonly IVisitManager visitManager;
        private readonly IVisitMasterTaxManager visitMasterTaxManager;
        private readonly ISLKP_BillingInvalidReasonManager sLKP_BillingInvalidReasonManager;
        private readonly ICashedOrderLineManager cashedOrderLineManager;
        private readonly IVisitData visitData;
        private readonly IVisitAppointmentData visitAppointmentData;
        private readonly IBillingInqueryBusinessRule billingInqueryBusinessRule;
        private readonly IVisitServiceData visitServiceData;

        public BillingInqueryManagerFactory(IVMUnitOfWork vMUnitOfWork,
                                            IBillingInqueryManager billingInqueryManager,
                                            IVisitServiceManager visitServiceManager,
                                            IVisitNoteManager visitNoteManager,
                                            IVisitManager visitManager,
                                            IVisitMasterTaxManager visitMasterTaxManager,
                                            ISLKP_BillingInvalidReasonManager sLKP_BillingInvalidReasonManager,
                                            ICashedOrderLineManager cashedOrderLineManager,
                                            IVisitData visitData,
                                            IVisitAppointmentData visitAppointmentData,
                                            IBillingInqueryBusinessRule billingInqueryBusinessRule,
                                            IVisitServiceData visitServiceData)
        {
            this.vMUnitOfWork = vMUnitOfWork;
            this.billingInqueryManager = billingInqueryManager;
            this.visitServiceManager = visitServiceManager;
            this.visitNoteManager = visitNoteManager;
            this.visitManager = visitManager;
            this.visitMasterTaxManager = visitMasterTaxManager;
            this.sLKP_BillingInvalidReasonManager = sLKP_BillingInvalidReasonManager;
            this.cashedOrderLineManager = cashedOrderLineManager;
            this.visitData = visitData;
            this.visitAppointmentData = visitAppointmentData;
            this.billingInqueryBusinessRule = billingInqueryBusinessRule;
            this.visitServiceData = visitServiceData;
        }

        /// <summary>
        /// Map Visit Services Model To Price Inquery Model
        /// </summary>
        /// <param name="notValidBillingServices"></param>
        /// <returns></returns>
        public List<PriceInquiryVisitMngParameters> MapVisitServicesToPriceInqueryModel(IEnumerable<VisitServiceDataModel> notValidBillingServices, bool notSendVisitServiceID = false)
        {
            try
            {
                VisitFinincailInfo financialInfoModel = new VisitFinincailInfo();
                List<PriceInquiryVisitMngParameters> inquiryVisitMngParameters = new List<PriceInquiryVisitMngParameters>();
                PriceInquiryVisitMngParameters priceInquiryVisitMng = new PriceInquiryVisitMngParameters();

                bool IsInsured = notValidBillingServices.FirstOrDefault().Visit.FinancialStatusID == (int)VMEnums.FinancialState.Insured;
                if (IsInsured)
                {
                    financialInfoModel = notValidBillingServices.FirstOrDefault().Visit.VisitFinincailInfoes.FirstOrDefault();
                }
                var serviceIds = notValidBillingServices.Where(s => (s.OrderlineID ?? 0) > 0).Select(s => s.ID).ToList();
                List<CashedOrderLine> cashedOrderLines = cashedOrderLineManager.GetCashedOrderLines(serviceIds);
                CashedOrderLine cashedOrderLine = new CashedOrderLine();

                foreach (var item in notValidBillingServices)
                {
                    cashedOrderLine = cashedOrderLines.Where(c => c.OrderLineID == item.OrderlineID && c.ProductCLassificationID == item.VisitServiceClassificationID).FirstOrDefault();

                    if (cashedOrderLine != null)
                    {
                        cashedOrderLine.OrderLineChargeStatusId = (int)VMEnums.OrderLineChargeStatusId.Charged;
                        item.ApprovalNotes = cashedOrderLine.ApprovalNotes;
                        //item.ApprovalLetterStatusID = cashedOrderLine.ApprovalLetterStatusID;
                        item.ManualIsNeedApproval = cashedOrderLine.ManualIsNeedApproval;

                        if (cashedOrderLine.ApprovalLetterStatusID == (int)VMEnums.ApprovalLetterStatus.Approved && cashedOrderLine.CoverLetterId > 0)
                        {
                            item.AuthorizationLetterId = cashedOrderLine.CoverLetterId;
                            item.ApprovalLetterStatusID = (int)VMEnums.ApprovalLetterStatus.Approved;
                            //item.DocumentNumber = cashedOrderLine.DocNumber;
                            item.DocumentDate = cashedOrderLine.DocDate;
                        }

                        if (cashedOrderLine.ApprovalLetterStatusID == (int)VMEnums.ApprovalLetterStatus.Rejected)
                        {
                            item.ApprovedQuantity = null;
                        }
                    }

                    Visit Visit = item.Visit;

                    VW_MPI_Patient Patient = visitData.GetPatientyByID(Visit.PatientID.Value);

                    Vw_Opd_Appointment Appointment = null;
                    VisitAppointment App = Visit.VisitAppointments.FirstOrDefault();
                    if (App != null)
                    {
                        Appointment = visitAppointmentData.GetAppointmentByID(App.AppointmentID.Value);
                    }

                    priceInquiryVisitMng = new PriceInquiryVisitMngParameters()
                    {
                        ProductID = item.ServiceID.Value,
                        FinancialStatus = Visit.FinancialStatusID.Value,
                        PurchasingDate = Visit.CreatedDate,
                        PerformingDate = item.EffectiveDate.HasValue ? item.EffectiveDate.Value : item.ClaimDate,
                        ProductUnitID = item.UnitID,
                        ProductQuantity = item.Quantity,
                        PatientID = Visit.PatientID,
                        PatientName = Patient.PatientEnName,
                        VisitID = item.VisitID,
                        SupervisoryLevel = Visit.VisitClassificationID == (int)VMEnums.VisitClasificationLookup.Inpatient ?
                (item.VisitServicePerformer != null ? item.VisitServicePerformer.SupervisoryLevelID : Visit.VisitAdmission.AdmittedDrSupervisoryLevelID)
                : Appointment != null ? Appointment.SupervisoryLevelID : null,
                        AccommodationClass = item.RoomClassID,
                        EpisodeType = Visit.VisitClassificationID,
                        Physician = Visit.VisitClassificationID == (int)VMEnums.VisitClasificationLookup.Inpatient ?
                (item.VisitServicePerformer != null ? item.VisitServicePerformer.PhysicianID : Visit.VisitAdmission.AdmittedDoctorID)
                : Appointment != null ? Appointment.PhysicianID : 0,

                        Specialty = Visit.VisitClassificationID == (int)VMEnums.VisitClasificationLookup.Inpatient ?
                (item.VisitServicePerformer != null ? item.VisitServicePerformer.SpecialtyID : Visit.VisitAdmission.SpecialtyID)
                : Visit.SpecialtyID,

                        OperationClassification = (item.ServedByID == (int)VMEnums.ServedBy.Operation && item.VisitServiceOperationReservations.Any()) ?
                item.VisitServiceOperationReservations.Select(a => a.OperationReservationDetail.SeverityID).FirstOrDefault() : null,
                        Location = Appointment != null ? Appointment.LocationId : null,
                        Device = null,
                        ContractID = IsInsured ? financialInfoModel.ContractID : null,
                        InsuranceCardID = IsInsured ? financialInfoModel.InsuranceCardID : null,
                        ContractorClientID = IsInsured ? financialInfoModel.ContractorClientID : null,
                        ContractorID = IsInsured ? financialInfoModel.ContractorID : null,
                        ContractCategoryID = IsInsured ? financialInfoModel.ContractCategoryID : null,
                        BeneficiaryTypeID = IsInsured ? financialInfoModel.BeneficiaryTypeID : null,
                        TransactionID = item.BillingTransactionId,
                        NationalityID = Patient.NationalityID,
                        CoverLetterID = item.AuthorizationLetterId,
                        AllowExpiry = IsInsured ? financialInfoModel.IsExpired : null,
                        VisitServiceID = notSendVisitServiceID ? 0 : item.ID,
                        Discount = (item.DiscountPercentage) ?? 0,
                        Barcode = item.Barcode,
                        PatientPackageID = item.VisitService.PatientPackageID,
                        IsNeedApproval = cashedOrderLine != null ? cashedOrderLine.ManualIsNeedApproval : item.ManualIsNeedApproval,
                        DiagnosesModel = Visit.Diagnoses == null ? null : Visit.Diagnoses.Select(d => new VisitDiagnosisModel
                        {
                            DiagnoseID = d.DiagnoseID,
                            ICDDiagnoseCode = d.ICDDiagnoseCode,
                            ICDDiagnoseName = d.ICDDiagnoseName
                        })
                    };
                    inquiryVisitMngParameters.Add(priceInquiryVisitMng);
                }

                return inquiryVisitMngParameters;
            }
            catch (Exception ex)
            {
                LogFactory<BillingInqueryManagerFactory>.LogException(ex);
                throw ex;
            }
        }

        /// <summary>
        /// Get Batch Pricing from biling to list of item 
        /// </summary>
        /// <param name="ModelBatchItemList"></param>
        public BatchPricingModel GetBatchPricing(List<VisitServiceCheckPriceModel> VisitServiceCheckPriceModel)
        {
            List<PriceInquiryVisitMngParameters> billingInqueryParams = new List<PriceInquiryVisitMngParameters>();

            BatchPricingModel batchPricingVisitServioces = new BatchPricingModel();
            try
            {
                if (VisitServiceCheckPriceModel[0].IsFromPrescription)
                {
                    Visit Visit = null;

                    if (VisitServiceCheckPriceModel[0].VisitID > 0)
                    {
                        Visit = visitManager.GetVisitByID(VisitServiceCheckPriceModel[0].VisitID);
                    }


                    billingInqueryParams = VisitServiceCheckPriceModel.Select(s => new PriceInquiryVisitMngParameters()
                    {
                        AllowExpiry = s.AllowExpiry,
                        BeneficiaryTypeID = s.BeneficiaryTypeID,
                        ContractCategoryID = s.ContractCategoryID,
                        ContractID = s.ContractID,
                        ContractorClientID = s.ContractorClientID,
                        ContractorID = s.ContractorID,
                        EpisodeType = null,
                        FinancialStatus = s.FinancialStatus,
                        InsuranceCardID = s.InsuranceCardID,
                        Location = s.Location,
                        NationalityID = s.NationalityID,
                        PatientID = s.PatientID,
                        PatientName = s.PatientName,
                        Physician = s.Physician,
                        PurchasingDate = DateTime.Now,
                        Specialty = null,
                        SupervisoryLevel = s.SupervisoryLevel,
                        VisitID = s.VisitID,
                        ProductID = s.ServiceID,
                        PerformingDate = DateTime.Now,
                        ProductUnitID = s.ServiceUnitID,
                        ProductQuantity = s.ServiceQuantity,
                        Barcode = s.Barcode,

                        AccommodationClass = null,
                        OperationClassification = null,
                        Device = null,
                        TransactionID = null,
                        CoverLetterID = null,
                        VisitServiceID = 0,
                        Discount = 0,
                        DiagnosesModel = Visit == null ? null : Visit.Diagnoses == null ? null : Visit.Diagnoses.Select(d => new VisitDiagnosisModel
                        {
                            DiagnoseID = d.DiagnoseID,
                            ICDDiagnoseCode = d.ICDDiagnoseCode,
                            ICDDiagnoseName = d.ICDDiagnoseName
                        })
                    }).ToList();

                }
                else
                {
                    Visit visitDetails = visitManager.GetVisitByID(VisitServiceCheckPriceModel.FirstOrDefault().VisitID);
                    VisitFinincailInfo financialInfoModel = new VisitFinincailInfo();
                    
                    bool IsInsured = visitDetails.FinancialStatusID == (int)VMEnums.FinancialState.Insured;
                    if (IsInsured)
                    {                        
                        financialInfoModel = visitDetails.VisitFinincailInfoes.FirstOrDefault();
                    }

                    VW_MPI_Patient Patient = visitData.GetPatientyByID(visitDetails.PatientID.Value);

                    Vw_Opd_Appointment Appointment = null;
                    VisitAppointment App = visitDetails.VisitAppointments.FirstOrDefault();
                    if (App != null)
                    {
                        Appointment = visitAppointmentData.GetAppointmentByID(App.AppointmentID.Value);
                    }

                    billingInqueryParams = VisitServiceCheckPriceModel.Select(s => new PriceInquiryVisitMngParameters()
                    {

                        FinancialStatus = visitDetails.FinancialStatusID.Value,
                        PurchasingDate = visitDetails.CreatedDate,
                        PatientID = visitDetails.PatientID,
                        PatientName = Patient.PatientEnName,
                        VisitID = visitDetails.ID,
                        EpisodeType = visitDetails.VisitClassificationID,

                        Physician = visitDetails.VisitClassificationID == (int)VMEnums.VisitClasificationLookup.Inpatient
                            ? visitDetails.VisitAdmission.AdmittedDoctorID
                            : Appointment != null ? Appointment.PhysicianID : 0,

                        Specialty = visitDetails.VisitClassificationID == (int)VMEnums.VisitClasificationLookup.Inpatient
                            ? visitDetails.VisitAdmission.SpecialtyID
                            : visitDetails.SpecialtyID,

                        SupervisoryLevel = visitDetails.VisitClassificationID == (int)VMEnums.VisitClasificationLookup.Inpatient
                            ? visitDetails.VisitAdmission.AdmittedDrSupervisoryLevelID
                            : Appointment != null ? Appointment.SupervisoryLevelID : null,

                        Location = Appointment != null ? Appointment.LocationId : null,
                        NationalityID = Patient.NationalityID,

                        ContractID = IsInsured ? financialInfoModel.ContractID : null,
                        InsuranceCardID = IsInsured ? financialInfoModel.InsuranceCardID : null,
                        ContractorClientID = IsInsured ? financialInfoModel.ContractorClientID : null,
                        ContractorID = IsInsured ? financialInfoModel.ContractorID : null,
                        ContractCategoryID = IsInsured ? financialInfoModel.ContractCategoryID : null,
                        BeneficiaryTypeID = IsInsured ? financialInfoModel.BeneficiaryTypeID : null,
                        AllowExpiry = IsInsured ? financialInfoModel.IsExpired : null,

                        PatientPackageID = s.PatientPackageID,
                        ProductID = s.ServiceID,
                        PerformingDate = DateTime.Now,
                        ProductUnitID = s.ServiceUnitID,
                        ProductQuantity = s.ServiceQuantity,
                        Barcode = s.Barcode,

                        AccommodationClass = visitDetails.VisitClassificationID == (int)VMEnums.VisitClasificationLookup.Inpatient
                            ? visitDetails.VisitPatientAccommodations.LastOrDefault() != null
                                ? visitDetails.VisitPatientAccommodations.LastOrDefault().EffectiveClassID
                                : null
                            : null,

                        OperationClassification = null,
                        Device = null,
                        TransactionID = null,
                        CoverLetterID = null,
                        VisitServiceID = 0,
                        Discount = 0,
                        DiagnosesModel = visitDetails.Diagnoses == null ? null : visitDetails.Diagnoses.Select(d => new VisitDiagnosisModel
                        {
                            DiagnoseID = d.DiagnoseID,
                            ICDDiagnoseCode = d.ICDDiagnoseCode,
                            ICDDiagnoseName = d.ICDDiagnoseName
                        })
                    }).ToList();

                }

                // Log Parameters In Log File
                LogFactory<BillingInqueryManagerFactory>.Log(LogLevel.Info, JsonConvert.SerializeObject(billingInqueryParams), LogType.File);

                // Calling Billing
                List<PriceInquiryVisitMngModel> billingResult = billingInqueryManager.PriceInqueryBeforeSaveService(billingInqueryParams).ToList();

                for (int i = 0; i < billingResult.Count; i++)
                {
                    PricedServiceModel PricedService = new PricedServiceModel();
                    PricedService.RowID = VisitServiceCheckPriceModel[i].RowID;
                    PricedService.Price = billingResult[i].Price.HasValue ? billingResult[i].Price.Value : 0;
                    PricedService.PatientShareAmount = billingResult[i].PatientShare.HasValue ? billingResult[i].PatientShare.Value : 0;
                    PricedService.CompanyShareAmount = billingResult[i].CompanyShare.HasValue ? billingResult[i].CompanyShare.Value : 0;
                    PricedService.PackageShareAmount = billingResult[i].PackageShare.HasValue ? billingResult[i].PackageShare.Value : 0;
                    PricedService.BillingInvalidReason = sLKP_BillingInvalidReasonManager.GetBillingInvalidReasonDisplayName((int)billingResult[i].StatusCode); // billingResult[i].StatusCode.ToString();
                    PricedService.hasBillingError = (int)billingResult[i].StatusCode != (int)VMEnums.BillingEngineStatusCode.Valid ? true : false;
                    PricedService.CoveredQuantity = billingResult[i].CoveredQuantity;
                    PricedService.ContractVisitLimit = billingResult[i].ContractVisitLimit;
                    PricedService.CoveredPriceBeforeDiscount = billingResult[i].CoveredPriceBeforeDiscount;
                    PricedService.CoveredPatientShare = billingResult[i].CoveredPatientShare;
                    PricedService.CoveredCompanyDiscount = billingResult[i].CoveredCompanyDiscount;
                    PricedService.NotCoveredCompanyDiscount = billingResult[i].NotCoveredCompanyDiscount;
                    PricedService.InsuredPriceBeforeDiscount = billingResult[i].InsuredPriceBeforeDiscount;
                    PricedService.CompanyDiscountAmount = billingResult[i].CompanyDiscountAmount;

                    batchPricingVisitServioces.PricedServices.Add(PricedService);
                }
                batchPricingVisitServioces.Totalprice = Math.Round(batchPricingVisitServioces.PricedServices.Sum(P => P.Price), 2);
                batchPricingVisitServioces.TotalPatientShareAmount = Math.Round(batchPricingVisitServioces.PricedServices.Sum(P => P.PatientShareAmount), 2);
                batchPricingVisitServioces.TotalCompanyShareAmount = Math.Round(batchPricingVisitServioces.PricedServices.Sum(P => P.CompanyShareAmount), 2);
                batchPricingVisitServioces.TotalPackageShareAmount = Math.Round(batchPricingVisitServioces.PricedServices.Sum(P => P.PackageShareAmount), 2);

            }
            catch (Exception ex)
            {
                LogFactory<BillingInqueryManagerFactory>.LogException(ex);
            }
            return batchPricingVisitServioces;
        }

        /// <summary>
        /// Used In Create Visit
        /// </summary>
        /// <param name="visit"></param>
        public void BillingPriceInquery(Visit visit)
        {
            try
            {
                LogFactory<BillingInqueryManagerFactory>.Log(LogLevel.Info, "DTC Problem VM BillingPriceInquery Start" + Transaction.Current?.TransactionInformation?.CreationTime.ToString());
                LogFactory<BillingInqueryManagerFactory>.Log(LogLevel.Info, "DTC Problem VM BillingPriceInquery Start" + Transaction.Current?.TransactionInformation?.DistributedIdentifier.ToString());
                LogFactory<BillingInqueryManagerFactory>.Log(LogLevel.Info, "DTC Problem VM BillingPriceInquery Start" + Transaction.Current?.TransactionInformation?.LocalIdentifier.ToString());
                LogFactory<BillingInqueryManagerFactory>.Log(LogLevel.Info, "DTC Problem VM BillingPriceInquery Start" + Transaction.Current?.TransactionInformation?.Status.ToString());

                //IEnumerable<VisitService> VisitServices = Visit.VisitServices.Where(s => s.IsDeleted != true);

                var visitServiceDataModels = visitServiceData.GetFullData(x => x.VisitID == visit.ID && x.IsDeleted != true);
                var notValidBillingServices = visitServiceDataModels.Where(x => x.IsValidBillingState != true).ToList();
                //IEnumerable<VisitService> NotValidBillingServices = VisitServices.Where(vs => vs.IsValidBillingState == false);

                if (!notValidBillingServices.Any())
                {
                    return;
                }
                var notValidBillingServicesEntities = notValidBillingServices
                    .Select(x => visitServiceData.MapVisitService(x)).ToList();
                notValidBillingServicesEntities = SetIPDEffectiveClassData(visit, notValidBillingServicesEntities).ToList();
                List<CashedOrderLine> cashedOrderLines = cashedOrderLineManager.GetCashedOrderLines(notValidBillingServicesEntities);

                List<PriceInquiryVisitMngParameters> billingInqueryParams = MapVisitServicesAndOLsToPriceInqueryModel(visit, notValidBillingServices, cashedOrderLines);

                VisitBillingResponseWithTax visitbillingResultWithTax = billingInqueryManager.PriceInquery(billingInqueryParams, visit.ID);


                if (!visitbillingResultWithTax.PriceInquiryVisitMngModel.Any())
                {
                    return;
                }

                IEnumerable<long?> billingPricedServicesIDs = visitbillingResultWithTax.PriceInquiryVisitMngModel.Select(s => s.VisitServiceID);
                var allVisitServicesToPrice = visitServiceDataModels.Where(vs => billingPricedServicesIDs.Contains(vs.ID));

               var allServicesAfterUpdate = new List<VisitServiceDataModel>();

                // int servicesListCount = allVisitServicesToPrice.Count();
                var visitServicesIds = allVisitServicesToPrice.Select(x => x.ID).ToList();
                List<CashedOrderLine> cashedOrderLinesToPrice = cashedOrderLineManager.GetCashedOrderLines(visitServicesIds);

                CashedOrderLine cashedOrderLine = new CashedOrderLine();

                foreach (var currentService in allVisitServicesToPrice)
                {

                    if (cashedOrderLines.Any())
                    {
                        cashedOrderLine = cashedOrderLines.Where(c => c.OrderLineID == currentService.OrderlineID 
                        && c.ProductCLassificationID == currentService.VisitServiceClassificationID).FirstOrDefault();

                    }

                    PriceInquiryVisitMngModel serviceToUpdate = visitbillingResultWithTax.PriceInquiryVisitMngModel.FirstOrDefault(b => b.VisitServiceID == currentService.ID);
                    if(currentService != null && serviceToUpdate != null)
                    {
                        allServicesAfterUpdate.Add(UpdateVisitServiceWithBillingReponse(currentService, cashedOrderLine, serviceToUpdate));
                    }

                }

                if (visitbillingResultWithTax.MasterTaxs != null)
                {
                    billingInqueryBusinessRule.SaveNewCalculatedMasterTaxs(visit, visitbillingResultWithTax.MasterTaxs);
                }

                if (allServicesAfterUpdate.Any())
                {
                   var OLServices = allServicesAfterUpdate.Where(vs => vs.OrderlineID.HasValue && vs.OrderlineID > 0);

                    if (cashedOrderLinesToPrice.Any() && OLServices.Any())
                    {
                        visitServiceManager.updateCashedOrderLineWithBillingResponseInquery(allServicesAfterUpdate, cashedOrderLinesToPrice);
                    }
                }

                vMUnitOfWork.SaveChanges();

                LogFactory<BillingInqueryManagerFactory>.Log(LogLevel.Info, "DTC Problem VM BillingPriceInquery End" + Transaction.Current?.TransactionInformation?.CreationTime.ToString());
                LogFactory<BillingInqueryManagerFactory>.Log(LogLevel.Info, "DTC Problem VM BillingPriceInquery End" + Transaction.Current?.TransactionInformation?.DistributedIdentifier.ToString());
                LogFactory<BillingInqueryManagerFactory>.Log(LogLevel.Info, "DTC Problem VM BillingPriceInquery End" + Transaction.Current?.TransactionInformation?.LocalIdentifier.ToString());
                LogFactory<BillingInqueryManagerFactory>.Log(LogLevel.Info, "DTC Problem VM BillingPriceInquery End" + Transaction.Current?.TransactionInformation?.Status.ToString());
            }
            catch (Exception ex)
            {
                LogFactory<BillingInqueryManagerFactory>.LogException(ex);
                throw ex;
            }
        }

        private VisitServiceDataModel UpdateVisitServiceWithBillingReponse(VisitServiceDataModel currentService, CashedOrderLine cashedOrderLine, PriceInquiryVisitMngModel serviceToUpdate)
        {

            if (currentService == null)
            {
                return currentService;
            }

            if (serviceToUpdate == null)
            {
                return currentService;
            }

            //bool hasTaxs = serviceToUpdate.Taxes != null && serviceToUpdate.Taxes.Count > 0 ? true : false;

            //currentService.Price = serviceToUpdate.Price;
            //currentService.PatientShare = serviceToUpdate.PatientShare;
            //currentService.CompanyShare = serviceToUpdate.CompanyShare;
            //currentService.IsValidBillingState = serviceToUpdate.IsValid;
            currentService.VisitService.BillingTransactionId = serviceToUpdate.TransactionID;
            currentService.VisitService.BillingInvalidReason = (int)serviceToUpdate.StatusCode;
            currentService.VisitService.NeedApproval = serviceToUpdate.IsNeedApproval;
            //currentService.CompanyDiscount = serviceToUpdate.CompanyDiscount;
            //currentService.PriceBeforeDiscount = serviceToUpdate.PriceBeforeDiscount;
            //currentService.ServiceTaxDue = hasTaxs ? serviceToUpdate.Taxes.Sum(s => s.PatientTaxAmountDue) : 0;
            //currentService.InPackageQuantity = serviceToUpdate.Inquantity;
            //currentService.PackageShare = serviceToUpdate.PackageShare;
            //currentService.PackageDiscount = serviceToUpdate.PackageDiscount;
            currentService.VisitService.ApprovedQuantity = serviceToUpdate.CoveredQuantity;
            //currentService.ContractVisitLimit = serviceToUpdate.ContractVisitLimit;
            //currentService.CoveredPriceBeforeDiscount = serviceToUpdate.CoveredPriceBeforeDiscount;
            //currentService.CoveredPatientShare = serviceToUpdate.CoveredPatientShare;
            //currentService.CoveredCompanyDiscount = serviceToUpdate.CoveredCompanyDiscount;
            //currentService.NotCoveredCompanyDiscount = serviceToUpdate.NotCoveredCompanyDiscount;
            //currentService.CompanyDiscountAmount = serviceToUpdate.CompanyDiscountAmount;
            //currentService.InsuredPriceBeforeDiscount = serviceToUpdate.InsuredPriceBeforeDiscount;
            currentService.VisitService.ShowinAuthorization = serviceToUpdate.ShowinAuthorization;
            currentService.VisitService.DueAmount = serviceToUpdate.PatientShare ?? 0 - currentService.DiscountAmount ?? 0;

            if (cashedOrderLine != null)
            {
                cashedOrderLine.ShowInAuthorization = serviceToUpdate.ShowinAuthorization;
                cashedOrderLine.CompanyDiscount = serviceToUpdate.CompanyDiscount;
            }

            //visitServiceManager.UpdateServiceTax(serviceToUpdate, currentService);

            return currentService;
        }

        private IEnumerable<VisitService> SetIPDEffectiveClassData(Visit Visit, IEnumerable<VisitService> NotValidBillingServices)
        {
            if (Visit.VisitClassificationID != (int)VMEnums.VisitClasificationLookup.Inpatient)
            {
                return NotValidBillingServices;
            }

            List<VisitPatientAccommodation> VisitAccommodations = Visit.VisitPatientAccommodations.ToList();

            foreach (VisitService service in NotValidBillingServices)
            {
                if (service.CreatedDate.HasValue && service.MappingTypeID == (int)VMEnums.MappingType.Ruled)
                {
                    continue;
                }

                DateTime StartDate = service.CreatedDate.Value;
                VisitPatientAccommodation PatientAccomodation = VisitAccommodations
                    .FirstOrDefault(v => v.AccommodationStartDateTime <= StartDate && (v.AccommodationEndDateTime ?? DateTime.MaxValue) >= StartDate);

                if (PatientAccomodation == null)
                {
                    continue;
                }

                service.RoomClassID = PatientAccomodation.EffectiveClassID;
                service.RoomClassArName = PatientAccomodation.EffectiveClassArName;
                service.RoomClassEnName = PatientAccomodation.EffectiveClassEnName;
            }

            return NotValidBillingServices;
        }

        public List<PriceInquiryVisitMngParameters> MapVisitServicesAndOLsToPriceInqueryModel(Visit Visit, IEnumerable<VisitServiceDataModel> notValidBillingServices, IEnumerable<CashedOrderLine> cashedOrderLines, bool notSendVisitServiceID = false)
        {
            try
            {
                VisitFinincailInfo financialInfoModel = new VisitFinincailInfo();
                List<PriceInquiryVisitMngParameters> inquiryVisitMngParameters = new List<PriceInquiryVisitMngParameters>();
                PriceInquiryVisitMngParameters priceInquiryVisitMng = new PriceInquiryVisitMngParameters();

                bool IsInsured = Visit.VisitFinincailInfoes.Any();
                if (IsInsured)
                {
                    financialInfoModel = Visit.VisitFinincailInfoes.FirstOrDefault();
                }
                CashedOrderLine cashedOrderLine = new CashedOrderLine();

                foreach (var item in notValidBillingServices)
                {
                    cashedOrderLine = cashedOrderLines.Where(c => c.OrderLineID == item.OrderlineID && c.ProductCLassificationID == item.VisitServiceClassificationID).FirstOrDefault();

                    if (cashedOrderLine != null)
                    {
                        cashedOrderLine.OrderLineChargeStatusId = (int)VMEnums.OrderLineChargeStatusId.Charged;
                        item.ApprovalNotes = cashedOrderLine.ApprovalNotes;
                        //item.ApprovalLetterStatusID = cashedOrderLine.ApprovalLetterStatusID;
                        item.ManualIsNeedApproval = cashedOrderLine.ManualIsNeedApproval;

                        if (cashedOrderLine.ApprovalLetterStatusID == (int)VMEnums.ApprovalLetterStatus.Approved && cashedOrderLine.CoverLetterId > 0)
                        {
                            item.AuthorizationLetterId = cashedOrderLine.CoverLetterId;
                            //item.ApprovalLetterStatusID = (int)VMEnums.ApprovalLetterStatus.Approved;
                            //item.DocumentNumber = cashedOrderLine.DocNumber;
                            item.DocumentDate = cashedOrderLine.DocDate;
                        }

                        if (cashedOrderLine.ApprovalLetterStatusID == (int)VMEnums.ApprovalLetterStatus.Rejected)
                        {
                            item.ApprovedQuantity = null;
                        }
                    }

                    VW_MPI_Patient Patient = visitData.GetPatientyByID(Visit.PatientID.Value);

                    Vw_Opd_Appointment Appointment = null;
                    VisitAppointment App = Visit.VisitAppointments.FirstOrDefault();
                    if (App != null)
                    {
                        Appointment = visitAppointmentData.GetAppointmentByID(App.AppointmentID.Value);
                    }

                    priceInquiryVisitMng = new PriceInquiryVisitMngParameters()
                    {
                        ProductID = item.ServiceID.Value,
                        FinancialStatus = Visit.FinancialStatusID.Value,
                        PurchasingDate = Visit.CreatedDate,
                        PerformingDate = item.EffectiveDate.HasValue ? item.EffectiveDate.Value : item.ClaimDate,
                        ProductUnitID = item.UnitID,
                        ProductQuantity = item.Quantity,
                        PatientID = Visit.PatientID,
                        PatientName = Patient.PatientEnName,
                        VisitID = item.VisitID,
                        ContractID = IsInsured ? financialInfoModel.ContractID : null,
                        SupervisoryLevel = Visit.VisitClassificationID == (int)VMEnums.VisitClasificationLookup.Inpatient
                            ? (item.VisitServicePerformer != null ? item.VisitServicePerformer.SupervisoryLevelID : Visit.VisitAdmission.AdmittedDrSupervisoryLevelID)
                            : Appointment != null ? Appointment.SupervisoryLevelID : null,
                        AccommodationClass = item.RoomClassID,
                        EpisodeType = Visit.VisitClassificationID,
                        Physician = Visit.VisitClassificationID == (int)VMEnums.VisitClasificationLookup.Inpatient
                            ? (item.VisitServicePerformer != null ? item.VisitServicePerformer.PhysicianID : Visit.VisitAdmission.AdmittedDoctorID)
                            : Appointment != null ? Appointment.PhysicianID : 0,
                        Specialty = Visit.VisitClassificationID == (int)VMEnums.VisitClasificationLookup.Inpatient
                            ? (item.VisitServicePerformer != null ? item.VisitServicePerformer.SpecialtyID : Visit.VisitAdmission.SpecialtyID)
                            : Visit.SpecialtyID,

                        OperationClassification = (item.ServedByID == (int)VMEnums.ServedBy.Operation && item.VisitServiceOperationReservations.Any())
                            ? item.VisitServiceOperationReservations.Select(a => a.OperationReservationDetail.SeverityID).FirstOrDefault()
                            : null,
                        Location = Appointment != null ? Appointment.LocationId : null,
                        Device = null,
                        InsuranceCardID = IsInsured ? financialInfoModel.InsuranceCardID : null,
                        ContractorClientID = IsInsured ? financialInfoModel.ContractorClientID : null,
                        ContractorID = IsInsured ? financialInfoModel.ContractorID : null,
                        ContractCategoryID = IsInsured ? financialInfoModel.ContractCategoryID : null,
                        BeneficiaryTypeID = IsInsured ? financialInfoModel.BeneficiaryTypeID : null,
                        TransactionID = item.BillingTransactionId,
                        NationalityID = Patient.NationalityID,
                        CoverLetterID = item.AuthorizationLetterId,
                        AllowExpiry = IsInsured ? financialInfoModel.IsExpired : null,
                        VisitServiceID = notSendVisitServiceID ? 0 : item.ID,
                        Discount = (item.DiscountPercentage) ?? 0,
                        Barcode = item.Barcode,
                        PatientPackageID = item.VisitService.PatientPackageID,
                        IsNeedApproval = cashedOrderLine != null ? cashedOrderLine.ManualIsNeedApproval : item.ManualIsNeedApproval,
                        DiagnosesModel = Visit.Diagnoses == null ? null : Visit.Diagnoses.Select(d => new VisitDiagnosisModel
                        {
                            DiagnoseID = d.DiagnoseID,
                            ICDDiagnoseCode = d.ICDDiagnoseCode,
                            ICDDiagnoseName = d.ICDDiagnoseName
                        })
                    };
                    inquiryVisitMngParameters.Add(priceInquiryVisitMng);
                }

                return inquiryVisitMngParameters;
            }
            catch (Exception ex)
            {
                LogFactory<BillingInqueryManagerFactory>.LogException(ex);
                throw ex;
            }
        }

        public VMEnums.SaveResult CallBillingForTransactionUpdate(Visit currentVisit, IEnumerable<VisitService> servicesToBeCancelledInBilling)
        {
            VMEnums.SaveResult result = new VMEnums.SaveResult();
            VisitBillingResponseWithTax visitbillingResultWithTax = billingInqueryManager.CallCancelPatientTransactionService(servicesToBeCancelledInBilling.Select(j => j.BillingTransactionId.Value).ToList(), currentVisit.ID);


            if (visitbillingResultWithTax.PriceInquiryVisitMngModel == null)
            {
                LogFactory<BillingInqueryManagerFactory>.Log(LogLevel.Error, VMExceptionMessagesEnum.FailureInBilling.ToString());
                return VMEnums.SaveResult.FailureInBilling;
            }

            List<CancelPatientTransactionResponse> servicesToBeUpdated = visitServiceManager.CancelPatientTransactionLogic(visitbillingResultWithTax.PriceInquiryVisitMngModel, currentVisit);
            if (servicesToBeUpdated.Any())
            {
                List<CancelPatientTransactionResponse> servicesToAddDiscount = servicesToBeUpdated.Where(x => x.RecalculatedDiscountFlag == true).ToList();
                if (servicesToAddDiscount.Any())
                {
                    visitNoteManager.ConstructVisitNoteForDiscount(currentVisit.ID, ConstModel.SystemRecalculatesTheDiscountAmountPatientShareChanged);
                }
            }

            if (visitbillingResultWithTax.MasterTaxs != null)
            {
                billingInqueryBusinessRule.SaveNewCalculatedMasterTaxs(currentVisit, visitbillingResultWithTax.MasterTaxs);
            }

            return result;
        }
    }
}
