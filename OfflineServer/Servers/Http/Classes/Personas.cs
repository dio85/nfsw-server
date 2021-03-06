﻿using OfflineServer.Data;
using OfflineServer.Servers.Database.Entities;
using OfflineServer.Servers.Database.Management;
using OfflineServer.Servers.Http.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace OfflineServer.Servers.Http.Classes
{
    public static class Personas
    {
        public static String baskets()
        {
            BasketTrans basketTrans = Access.sHttp.getPostData().DeserializeObject<BasketTrans>();
            CommerceResultTrans commerceResultTrans = new CommerceResultTrans();
            List<InventoryItemTrans> inventoryItems = new List<InventoryItemTrans>();
            List<OwnedCarTrans> purchasedCars = new List<OwnedCarTrans>();

            Economy economy = Economy.defineFromBasketItemTransList(basketTrans.basketItems);
            if (economy == null)
            {
                commerceResultTrans.status = Basket.ShoppingCartPurchaseResult.Fail_InvalidBasket;
            }
            else if (!economy.canBuy())
            {
                commerceResultTrans.status = Basket.ShoppingCartPurchaseResult.Fail_InsufficientFunds;
            }
            else
            {
                economy.doTransaction();
                commerceResultTrans.status = Basket.ShoppingCartPurchaseResult.Success;
                commerceResultTrans.wallets.walletTrans = new WalletTrans()
                {
                    balance = economy.balance,
                    currency = economy.currency
                };

                foreach (BasketItemTrans basketItemTrans in basketTrans.basketItems)
                {
                    for (int i = 0; i < basketItemTrans.quantity; i++)
                    {
                        switch (basketItemTrans.getItemType())
                        {
                            case Basket.BasketItemType.Unknown:
                                {
                                    commerceResultTrans.status = Basket.ShoppingCartPurchaseResult.Fail_ItemNotAvailableStandalone;
                                    goto finalize; // ahahah fuck you anti-goto people
                                }
                            case Basket.BasketItemType.THRevive:
                                // implement TH
                                break;
                            case Basket.BasketItemType.Repair:
                                {
                                    Access.CurrentSession.ActivePersona.SelectedCar.Durability = (Int16)100;
                                }
                                break;
                            case Basket.BasketItemType.CarSlot:
                                // implement carslots
                                break;
                            case Basket.BasketItemType.Powerup:
                                String powerupType = basketItemTrans.productId.Replace("SRV-POWERUP-", "");

                                // TODO: 
                                // expose to UI
                                // improve Persona.Inventory
                                InventoryItemEntity entity = PersonaManagement.persona.inventory.FirstOrDefault(ii => ii.entitlementTag == powerupType);
                                if (entity != null)
                                {
                                    entity = InventoryItemManagement.getInventoryItemEntity(entity.id);
                                    entity.remainingUseCount += DataEx.productInformations[basketItemTrans.productId].useCount;
                                    entity.setInventoryItemEntity();

                                    inventoryItems.Add(entity.getInventoryItemTrans());
                                }
                                break;
                            case Basket.BasketItemType.Car:
                                {
                                    OwnedCarTrans purchasedCar = Catalog.getCarBasketDefinition(basketItemTrans.productId);
                                    if (purchasedCar == null)
                                        continue;

                                    CarEntity carEntity = new CarEntity();
                                    carEntity.baseCarId = purchasedCar.customCar.baseCarId;
                                    carEntity.carClassHash = purchasedCar.customCar.carClassHash;
                                    carEntity.durability = purchasedCar.durability;
                                    carEntity.heatLevel = purchasedCar.heatLevel;
                                    carEntity.name = purchasedCar.customCar.name;
                                    carEntity.paints = purchasedCar.customCar.paints.SerializeObject();
                                    carEntity.performanceParts = purchasedCar.customCar.performanceParts.SerializeObject();
                                    carEntity.physicsProfileHash = purchasedCar.customCar.physicsProfileHash;
                                    carEntity.rating = purchasedCar.customCar.rating;
                                    carEntity.resalePrice = purchasedCar.customCar.resalePrice;
                                    carEntity.skillModParts = purchasedCar.customCar.skillModParts.SerializeObject();
                                    carEntity.vinyls = purchasedCar.customCar.vinyls.SerializeObject();
                                    carEntity.visualParts = purchasedCar.customCar.visualParts.SerializeObject();

                                    carEntity = PersonaManagement.addCar(carEntity);

                                    purchasedCar.id = carEntity.id;
                                    purchasedCar.customCar.id = carEntity.id;
                                    purchasedCars.Add(purchasedCar);
                                }
                                break;
                        }
                    }
                }
            }

        finalize:
            commerceResultTrans.commerceItems = new List<CommerceItemTrans>();
            commerceResultTrans.inventoryItems = inventoryItems.Count > 0 ? inventoryItems : new List<InventoryItemTrans>() { new InventoryItemTrans() };
            commerceResultTrans.purchasedCars = purchasedCars;
            return commerceResultTrans.SerializeObject();
        }

        public static String cars()
        {
            if (Access.sHttp.request.HttpMethod == "POST")
            {
                if (Access.CurrentSession.ActivePersona.Cars.Count > 1)
                {
                    Int32 carId = Int32.Parse(Access.sHttp.request.Params.Get("serialNumber"));
                    Car curCar = Access.CurrentSession.ActivePersona.Cars.First(c => c.Id == carId);
                    Int32 curCarIndex = Access.CurrentSession.ActivePersona.Cars.IndexOf(curCar);

                    Economy economy = Economy.defineManually(new Economy.ProductInformation() { currency = Basket.Currency.Cash, price = curCar.ResalePrice }, 1);
                    economy.doIncrement();
                    PersonaManagement.removeCar(curCar);

                    Int32 newIndex;
                    if (Access.CurrentSession.ActivePersona.Cars.Count <= curCarIndex)
                        newIndex = Access.CurrentSession.ActivePersona.Cars.Count - 1;
                    else
                        newIndex = curCarIndex;
                    Access.CurrentSession.ActivePersona.CurrentCarIndex = newIndex;

                    return Access.CurrentSession.ActivePersona.SelectedCar.getOwnedCarTrans().SerializeObject();
                }
            }
            else if (Access.sHttp.request.HttpMethod == "PUT")
            {
                // TODO: Make performance parts use economy -> attribHash
                Car curCar = Access.CurrentSession.ActivePersona.Cars[Access.CurrentSession.ActivePersona.CurrentCarIndex];
                OwnedCarTrans newCar = Access.sHttp.getPostData().DeserializeObject<OwnedCarTrans>();

                curCar.PerformanceParts = XElement.Parse(newCar.customCar.performanceParts.SerializeObject());
                return curCar.getOwnedCarTrans().SerializeObject();
            }
            return "";
        }

        public static String carslots()
        {
            return Access.CurrentSession.ActivePersona.getCompleteGarage();
        }

        public static String commerce()
        {
            CommerceSessionTrans commerceSessionTrans = Access.sHttp.getPostData().DeserializeObject<CommerceSessionTrans>();
            CommerceSessionResultTrans commerceSessionResultTrans = new CommerceSessionResultTrans();
            commerceSessionResultTrans.status = Basket.ShoppingCartPurchaseResult.Fail_InvalidBasket;

            Car curCar = Access.CurrentSession.ActivePersona.Cars[Access.CurrentSession.ActivePersona.CurrentCarIndex];

            UpdatedCar responseCar = new UpdatedCar();
            responseCar.customCar = curCar.getCustomCar();
            responseCar.durability = curCar.Durability;
            responseCar.heatLevel = 1;
            responseCar.id = curCar.Id;
            responseCar.ownershipType = "CustomizedCar";

            Economy economy = Economy.defineFromBasketItemTransList(commerceSessionTrans.basketTrans.basketItems);
            if (economy.canBuy())
            {
                economy.doTransaction();
                commerceSessionResultTrans.status = Basket.ShoppingCartPurchaseResult.Success;
                commerceSessionResultTrans.wallets.walletTrans = new WalletTrans() { balance = economy.balance, currency = economy.currency };

                UpdatedCar newCar = commerceSessionTrans.updatedCar;
                curCar.Vinyls = XElement.Parse(newCar.customCar.vinyls.SerializeObject());
                curCar.Paints = XElement.Parse(newCar.customCar.paints.SerializeObject());
                curCar.PerformanceParts = XElement.Parse(newCar.customCar.performanceParts.SerializeObject());
                curCar.SkillModParts = XElement.Parse(newCar.customCar.skillModParts.SerializeObject());
                curCar.VisualParts = XElement.Parse(newCar.customCar.visualParts.SerializeObject());
                curCar.HeatLevel = 1;

                responseCar.customCar = newCar.customCar;
            }
            else
            {
                commerceSessionResultTrans.status = Basket.ShoppingCartPurchaseResult.Fail_InsufficientFunds;
            }

            commerceSessionResultTrans.inventoryItems = new List<InventoryItemTrans>();
            commerceSessionResultTrans.updatedCar = responseCar;
            return commerceSessionResultTrans.SerializeObject();
        }

        public static String defaultcar()
        {
            String[] splittedPath = Access.sHttp.request.Path.Split('/');
            if (splittedPath.Length == 6)
            {
                Int32 newCarId = Int32.Parse(splittedPath[5]);
                Access.CurrentSession.ActivePersona.CurrentCarIndex =
                    Access.CurrentSession.ActivePersona.Cars.IndexOf(
                        Access.CurrentSession.ActivePersona.Cars.First(c => c.Id == newCarId));

                return "";
            }

            Persona ownerPersona = Access.CurrentSession.PersonaList.First(p => p.Id == Int32.Parse(splittedPath[3]));
            if (ownerPersona.SelectedCar != null)
                return ownerPersona.SelectedCar.getCarEntry().ToString();

            return "";
        }

        public static String inventory()
        {
            return Access.CurrentSession.ActivePersona.getCompleteInventory().SerializeObject();
        }
    }
}
