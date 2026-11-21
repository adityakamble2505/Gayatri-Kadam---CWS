using GreenCarWash.BookingService.DTOs;
using GreenCarWash.BookingService.Services;
using GreenCarWash.BuildingBlocks.Pagination;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GreenCarWash.BookingService.Controllers;

[ApiController]
[Authorize]
[Route("api/bookings/reviews")]
public sealed class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;

    public ReviewsController(IReviewService reviews)
    {
        _reviews = reviews;
    }

    [HttpPost]
    [Authorize(Roles = "Customer,Washer")]
    public async Task<ActionResult<ReviewDto>> Create(CreateReviewDto dto, CancellationToken cancellationToken)
    {
        var review = await _reviews.CreateAsync(CurrentUser.From(User), dto, cancellationToken);
        return Created($"/api/bookings/reviews/{review.ReviewId}", review);
    }

    [HttpGet("washer/{washerId:int}")]
    public async Task<ActionResult<PagedResult<ReviewDto>>> ForWasher(
        int washerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _reviews.ForWasherAsync(washerId, page, pageSize, cancellationToken));
    }
}
