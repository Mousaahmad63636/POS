package com.spotlylb.admin.api

import com.spotlylb.admin.models.AuthRequest
import com.spotlylb.admin.models.AuthResponse
import com.spotlylb.admin.models.Order
import com.spotlylb.admin.models.StatusUpdateRequest
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.PUT
import retrofit2.http.Path

interface ApiService {

    @POST("users/login")
    suspend fun login(@Body request: AuthRequest): Response<AuthResponse>

    @GET("orders")
    suspend fun getOrders(): Response<List<Order>>

    @GET("orders/{id}")
    suspend fun getOrderById(@Path("id") orderId: String): Response<Order>

    @PUT("orders/{id}")
    suspend fun updateOrderStatus(
        @Path("id") orderId: String,
        @Body request: StatusUpdateRequest
    ): Response<Order>
}