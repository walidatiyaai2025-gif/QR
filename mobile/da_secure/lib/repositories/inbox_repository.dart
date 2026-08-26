import 'package:da_secure/models/mobile_models.dart';
import 'package:da_secure/networking/api_client.dart';
import 'package:da_secure/networking/app_failure.dart';
import 'package:dio/dio.dart';

class InboxRepository {
  const InboxRepository(this.client);

  final ApiClient client;

  Future<InboxPage> getInbox({
    int page = 1,
    int pageSize = 20,
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await client.get(
        '/api/mobile/inbox',
        queryParameters: {'page': page, 'pageSize': pageSize},
        cancelToken: cancelToken,
      );
      return InboxPage.fromJson(ApiClient.jsonMap(response.data));
    } on DioException catch (error) {
      throw ApiClient.mapError(error);
    } on FormatException {
      throw AppFailure.invalidResponse();
    }
  }

  Future<DeliveryDetails> getDelivery(
    int deliveryId, {
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await client.get(
        '/api/mobile/inbox/$deliveryId',
        cancelToken: cancelToken,
      );
      final envelope = ApiClient.jsonMap(response.data);
      return DeliveryDetails.fromJson(ApiClient.jsonMap(envelope['delivery']));
    } on DioException catch (error) {
      throw ApiClient.mapError(error);
    } on FormatException {
      throw AppFailure.invalidResponse();
    }
  }

  Future<RevealGrant> authenticate({
    required int deliveryId,
    required String username,
    required String password,
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await client.post(
        '/api/mobile/inbox/$deliveryId/authenticate',
        data: {'username': username, 'password': password},
        cancelToken: cancelToken,
      );
      return RevealGrant.fromJson(ApiClient.jsonMap(response.data));
    } on DioException catch (error) {
      throw ApiClient.mapError(error);
    } on FormatException {
      throw AppFailure.invalidResponse();
    }
  }

  Future<SecureMessage> reveal({
    required int deliveryId,
    required String revealToken,
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await client.post(
        '/api/mobile/inbox/$deliveryId/reveal',
        data: {'revealToken': revealToken},
        cancelToken: cancelToken,
      );
      return SecureMessage.fromJson(ApiClient.jsonMap(response.data));
    } on DioException catch (error) {
      throw ApiClient.mapError(error);
    } on FormatException {
      throw AppFailure.invalidResponse();
    }
  }
}
