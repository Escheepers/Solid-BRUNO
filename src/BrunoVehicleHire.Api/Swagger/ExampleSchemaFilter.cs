using System.Text.Json.Nodes;
using BrunoVehicleHire.Application.Bookings.Commands;
using BrunoVehicleHire.Application.Bookings.Dtos;
using BrunoVehicleHire.Application.Customers.Commands;
using BrunoVehicleHire.Application.Customers.Dtos;
using BrunoVehicleHire.Application.Vehicles.Commands;
using BrunoVehicleHire.Application.Vehicles.Dtos;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BrunoVehicleHire.Api.Swagger;

/// <summary>
/// Supplies concrete example JSON for the three Create commands and their response DTOs, so Swagger
/// UI shows a realistic worked example instead of Swashbuckle's bare reflection-based
/// <c>"string"</c>/<c>0</c> placeholders. Lives entirely in the Api project (spec-swagger-examples'
/// Boundaries) -- the Application-layer Commands/DTOs it targets carry zero Swagger-specific
/// attributes or references, keeping Clean Architecture's layering intact.
/// </summary>
public class ExampleSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concreteSchema)
        {
            return;
        }

        var example = context.Type switch
        {
            var t when t == typeof(CreateVehicleCommand) => CreateVehicleCommandExample,
            var t when t == typeof(VehicleDto) => VehicleDtoExample,
            var t when t == typeof(CreateCustomerCommand) => CreateCustomerCommandExample,
            var t when t == typeof(CustomerDto) => CustomerDtoExample,
            var t when t == typeof(CreateBookingCommand) => CreateBookingCommandExample,
            var t when t == typeof(BookingDto) => BookingDtoExample,
            _ => null,
        };

        if (example is not null)
        {
            concreteSchema.Example = example();
        }
    }

    private static readonly Func<JsonNode> CreateVehicleCommandExample = () => new JsonObject
    {
        ["registrationNumber"] = "CA 123-456",
        ["make"] = "Toyota",
        ["model"] = "Corolla",
        ["year"] = 2022,
        ["dailyRate"] = 450.00,
    };

    private static readonly Func<JsonNode> VehicleDtoExample = () => new JsonObject
    {
        ["id"] = "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        ["registrationNumber"] = "CA 123-456",
        ["make"] = "Toyota",
        ["model"] = "Corolla",
        ["year"] = 2022,
        ["dailyRate"] = 450.00,
        ["createdDate"] = "2026-01-15T09:30:00Z",
        ["isDeleted"] = false,
    };

    private static readonly Func<JsonNode> CreateCustomerCommandExample = () => new JsonObject
    {
        ["firstName"] = "Thandiwe",
        ["lastName"] = "Mokoena",
        ["email"] = "thandiwe.mokoena@example.com",
        ["phoneNumber"] = "+27 82 555 0142",
    };

    private static readonly Func<JsonNode> CustomerDtoExample = () => new JsonObject
    {
        ["id"] = "7c9e6679-7425-40de-944b-e07fc1f90ae7",
        ["firstName"] = "Thandiwe",
        ["lastName"] = "Mokoena",
        ["email"] = "thandiwe.mokoena@example.com",
        ["phoneNumber"] = "+27 82 555 0142",
        ["createdDate"] = "2026-01-15T09:30:00Z",
        ["isDeleted"] = false,
        ["isAnonymized"] = false,
    };

    private static readonly Func<JsonNode> CreateBookingCommandExample = () => new JsonObject
    {
        ["vehicleId"] = "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        ["customerId"] = "7c9e6679-7425-40de-944b-e07fc1f90ae7",
        ["startDate"] = "2026-03-10",
        ["endDate"] = "2026-03-14",
    };

    private static readonly Func<JsonNode> BookingDtoExample = () => new JsonObject
    {
        ["id"] = "9b2e6d3a-1c4f-4a8e-9d2b-6f3a1e5c7d90",
        ["vehicleId"] = "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        ["vehicleMake"] = "Toyota",
        ["vehicleModel"] = "Corolla",
        ["vehicleRegistrationNumber"] = "CA 123-456",
        ["customerId"] = "7c9e6679-7425-40de-944b-e07fc1f90ae7",
        ["customerFirstName"] = "Thandiwe",
        ["customerLastName"] = "Mokoena",
        ["customerIsAnonymized"] = false,
        ["startDate"] = "2026-03-10",
        ["endDate"] = "2026-03-14",
        ["totalPrice"] = 1800.00,
        ["status"] = "Active",
        ["createdDate"] = "2026-01-15T09:30:00Z",
    };
}
